﻿using KeePass.Plugins;
using KeePass.UI;
using KeePass.Util.Spr;
using KeePassNatMsg.Protocol;
using KeePassNatMsg.Protocol.Action;
using KeePassLib;
using KeePassLib.Collections;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using KeePassLib.Utility;
using KeePassLib.Delegates;

namespace KeePassNatMsg.Entry
{
    public sealed class EntrySearch
    {
        private const string TotpKey = "TimeOtp-Secret";
        private const string TotpPlaceholder = "{TIMEOTP}";
        private const string TotpLegacyPlaceholder = "{TOTP}";

        private readonly IPluginHost _host;
        private readonly KeePassNatMsgExt _ext;
        private readonly List<string> _allowedSchemes = new List<string>(new[] { "http", "https", "ftp", "sftp" });

        public EntrySearch()
        {
            _host = KeePassNatMsgExt.HostInstance;
            _ext = KeePassNatMsgExt.ExtInstance;
        }

        internal Response GetLoginsHandler(Request req)
        {
            if (!req.TryDecrypt()) return new ErrorResponse(req, ErrorType.CannotDecryptMessage);

            var msg = req.Message;
            var id = msg.GetString("id");
            var url = msg.GetString("url");
            var submitUrl = msg.GetString("submitUrl");

            Uri hostUri;
            Uri submitUri = null;

            if (!string.IsNullOrEmpty(url))
            {
                hostUri = new Uri(url);
            }
            else
            {
                return new ErrorResponse(req, ErrorType.NoUrlProvided);
            }

            if (!string.IsNullOrEmpty(submitUrl))
            {
                submitUri = new Uri(submitUrl);
            }

            var resp = req.GetResponse();
            resp.Message.Add("id", id);

            var items = FindMatchingEntries(url, null);
            if (items.ToList().Count > 0)
            {
                var filter = new GFunc<PwEntry, bool>((PwEntry e) =>
                {
                    var c = _ext.GetEntryConfig(e);

                    return c == null || (!c.Allow.Contains(hostUri.Authority)) || (submitUri != null && submitUri.Authority != null && !c.Allow.Contains(submitUri.Authority));
                });

                var configOpt = new ConfigOpt(_host.CustomConfig);
                var needPrompting = items.Where(e => filter(e.entry)).ToList();

                if (needPrompting.Count > 0 && !configOpt.AlwaysAllowAccess)
                {
                    var win = _host.MainWindow;

                    using (var f = new AccessControlForm())
                    {
                        win.Invoke((MethodInvoker)delegate
                        {
                            f.Icon = win.Icon;
                            f.Plugin = _ext;
                            f.StartPosition = win.Visible ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
                            f.Entries = needPrompting.Select(e => e.entry).ToList();
                            f.Host = submitUri != null ? submitUri.Authority : hostUri.Authority;
                            f.Load += delegate { f.Activate(); };
                            f.ShowDialog(win);
                            if (f.Remember && (f.Allowed || f.Denied))
                            {
                                foreach (var e in needPrompting)
                                {
                                    var c = _ext.GetEntryConfig(e.entry) ?? new EntryConfig();
                                    var set = f.Allowed ? c.Allow : c.Deny;
                                    set.Add(hostUri.Authority);
                                    if (submitUri != null && submitUri.Authority != null && submitUri.Authority != hostUri.Authority)
                                        set.Add(submitUri.Authority);
                                    _ext.SetEntryConfig(e.entry, c);
                                }
                            }
                            if (!f.Allowed)
                            {
                                items = items.Except(needPrompting);
                            }
                        });
                    }
                }

                var uri = submitUri != null ? submitUri : hostUri;

                foreach (var entryDatabase in items)
                {
                    string entryUrl = string.Copy(entryDatabase.entry.Strings.ReadSafe(PwDefs.UrlField));
                    if (string.IsNullOrEmpty(entryUrl))
                        entryUrl = entryDatabase.entry.Strings.ReadSafe(PwDefs.TitleField);

                    entryUrl = entryUrl.ToLower();

                    entryDatabase.entry.UsageCount = (ulong)LevenshteinDistance(uri.ToString().ToLower(), entryUrl);
                }

                var itemsList = items.ToList();

                if (configOpt.SpecificMatchingOnly)
                {
                    itemsList = (from e in itemsList
                                 orderby e.entry.UsageCount ascending
                                 select e).ToList();

                    ulong lowestDistance = itemsList.Count > 0 ?
                        itemsList[0].entry.UsageCount :
                        0;

                    itemsList = (from e in itemsList
                                 where e.entry.UsageCount == lowestDistance
                                 orderby e.entry.UsageCount
                                 select e).ToList();
                }

                if (configOpt.SortResultByUsername)
                {
                    var items2 = from e in itemsList orderby e.entry.UsageCount ascending, _ext.GetUserPass(e)[0] ascending select e;
                    itemsList = items2.ToList();
                }
                else
                {
                    var items2 = from e in itemsList orderby e.entry.UsageCount ascending, e.entry.Strings.ReadSafe(PwDefs.TitleField) ascending select e;
                    itemsList = items2.ToList();
                }

                var entries = new JArray(itemsList.Select(item =>
                {
                    var up = _ext.GetUserPass(item);
                    JArray fldArr = null;
                    var fields = GetFields(configOpt, item);
                    if (fields != null)
                    {
                        fldArr = new JArray(fields.Select(f => new JObject { { f.Key, f.Value } }));
                    }
                    var jobj = new JObject {
                        { "name", item.entry.Strings.ReadSafe(PwDefs.TitleField) },
                        { "login", up[0] },
                        { "password", up[1] },
                        { "uuid", item.entry.Uuid.ToHexString() },
                        { "stringFields", fldArr },
                    };

                    CheckTotp(item, jobj);

                    return jobj;
                }));

                resp.Message.Add("count", itemsList.Count);
                resp.Message.Add("entries", entries);

                if (itemsList.Count > 0)
                {
                    var names = (from e in itemsList select e.entry.Strings.ReadSafe(PwDefs.TitleField)).Distinct();
                    var n = String.Join("\n    ", names);

                    if (configOpt.ReceiveCredentialNotification)
                        _ext.ShowNotification(String.Format("{0}: {1} is receiving credentials for:\n    {2}", req.GetString("id"), hostUri.Host, n));
                }

                return resp;
            }

            resp.Message.Add("count", 0);
            resp.Message.Add("entries", new JArray());

            return resp;
        }

        private void CheckTotp(PwEntryDatabase item, JObject obj)
        {
            var totp = GetTotpFromEntry(item);
            if (!string.IsNullOrEmpty(totp))
            {
                obj.Add("totp", totp);
            }
        }

        internal string GetTotp(string uuid)
        {
            var dbEntry = FindEntry(uuid);

            if (dbEntry == null)
                return null;

            return GetTotpFromEntry(dbEntry);
        }

        private string GetTotpFromEntry(PwEntryDatabase item)
        {
            string totp = null;

            if (HasLegacyTotp(item.entry))
            {
                totp = GenerateTotp(item, TotpLegacyPlaceholder);
            }

            if (string.IsNullOrEmpty(totp) && HasTotp(item.entry))
            {
                // add support for keepass totp
                // https://keepass.info/help/base/placeholders.html#otp
                totp = GenerateTotp(item, TotpPlaceholder);
            }
            return totp;
        }

        private string GenerateTotp(PwEntryDatabase item, string placeholder)
        {
            var ctx = new SprContext(item.entry, item.database, SprCompileFlags.All, false, false);
            return SprEngine.Compile(placeholder, ctx);
        }

        private static bool HasTotp(PwEntry entry)
        {
            return entry.Strings.Any(x => x.Key.StartsWith(TotpKey));
        }

        // KeeOtp support through keepassxc-browser
        // KeeOtp stores the TOTP config in a string field "otp" and provides a placeholder "{TOTP}"
        // KeeTrayTOTP uses by default a "TOTP Seed" string field, and the {TOTP} placeholder.
        // keepassxc-browser needs the value in a string field named "KPH: {TOTP}"
        private static bool HasLegacyTotp(PwEntry entry)
        {
            return entry.Strings.Any(x =>
            x.Key.Equals("otp", StringComparison.InvariantCultureIgnoreCase) ||
            x.Key.Equals("TOTP Seed", StringComparison.InvariantCultureIgnoreCase));
        }

        private PwEntryDatabase FindEntry(string uuid)
        {
            PwUuid id = new PwUuid(MemUtil.HexStringToByteArray(uuid));

            var configOpt = new ConfigOpt(_host.CustomConfig);

            if (configOpt.SearchInAllOpenedDatabases)
            {
                foreach (var doc in _host.MainWindow.DocumentManager.Documents)
                {
                    if (doc.Database.IsOpen)
                    {
                        var entry = doc.Database.RootGroup.FindEntry(id, true);
                        if (entry != null)
                            return new PwEntryDatabase(entry, doc.Database);
                    }
                }
            }
            else
            {
                var entry = _host.Database.RootGroup.FindEntry(id, true);
                if (entry != null)
                    return new PwEntryDatabase(entry, _host.Database);
            }

            return null;
        }

        //http://en.wikibooks.org/wiki/Algorithm_Implementation/Strings/Levenshtein_distance#C.23
        private static int LevenshteinDistance(string source, string target)
        {
            if (String.IsNullOrEmpty(source))
            {
                if (String.IsNullOrEmpty(target)) return 0;
                return target.Length;
            }
            if (String.IsNullOrEmpty(target)) return source.Length;

            if (source.Length > target.Length)
            {
                var temp = target;
                target = source;
                source = temp;
            }

            var m = target.Length;
            var n = source.Length;
            var distance = new int[2, m + 1];
            // Initialize the distance 'matrix'
            for (var j = 1; j <= m; j++) distance[0, j] = j;

            var currentRow = 0;
            for (var i = 1; i <= n; ++i)
            {
                currentRow = i & 1;
                distance[currentRow, 0] = i;
                var previousRow = currentRow ^ 1;
                for (var j = 1; j <= m; j++)
                {
                    var cost = (target[j - 1] == source[i - 1] ? 0 : 1);
                    distance[currentRow, j] = Math.Min(Math.Min(
                                            distance[previousRow, j] + 1,
                                            distance[currentRow, j - 1] + 1),
                                            distance[previousRow, j - 1] + cost);
                }
            }
            return distance[currentRow, m];
        }

        private static IEnumerable<KeyValuePair<string, string>> GetFields(ConfigOpt configOpt, PwEntryDatabase entryDatabase)
        {
            SprContext ctx = new SprContext(entryDatabase.entry, entryDatabase.database, SprCompileFlags.All, false, false);

            List<KeyValuePair<string, string>> fields = null;
            if (configOpt.ReturnStringFields)
            {
                fields = new List<KeyValuePair<string, string>>();

                foreach (var sf in entryDatabase.entry.Strings)
                {
                    var sfValue = entryDatabase.entry.Strings.ReadSafe(sf.Key);

                    // follow references
                    sfValue = SprEngine.Compile(sfValue, ctx);

                    if (configOpt.ReturnStringFieldsWithKphOnly && sf.Key.StartsWith("KPH: "))
                    {
                        fields.Add(new KeyValuePair<string, string>(sf.Key.Substring(5), sfValue));
                    }
                    else
                    {
                        fields.Add(new KeyValuePair<string, string>(sf.Key, sfValue));
                    }
                }

                if (fields.Count > 0)
                {
                    var sorted = from e2 in fields orderby e2.Key ascending select e2;
                    fields = sorted.ToList();
                }
                else
                {
                    fields = null;
                }
            }

            return fields;
        }

        private IEnumerable<PwEntryDatabase> FindMatchingEntries(string url, string realm)
        {
            var listResult = new List<PwEntryDatabase>();
            var hostUri = new Uri(url);

            var formHost = hostUri.Host;
            var searchHost = hostUri.Host;
            var origSearchHost = hostUri.Host;

            List<PwDatabase> listDatabases = new List<PwDatabase>();

            var configOpt = new ConfigOpt(_host.CustomConfig);

            if (configOpt.MatchAuthorityUrl)
            {
                formHost = hostUri.Authority;
            }

            if (configOpt.SearchInAllOpenedDatabases)
            {
                foreach (PwDocument doc in _host.MainWindow.DocumentManager.Documents)
                {
                    if (doc.Database.IsOpen)
                    {
                        listDatabases.Add(doc.Database);
                    }
                }
            }
            else
            {
                listDatabases.Add(_host.Database);
            }

            var parms = MakeSearchParameters(configOpt.HideExpired);
            var searchUrls = configOpt.SearchUrls;
            int listCount = 0;

            foreach (PwDatabase db in listDatabases)
            {
                searchHost = origSearchHost;
                //get all possible entries for given host-name
                while (listResult.Count == listCount && (origSearchHost == searchHost || searchHost.IndexOf(".") != -1))
                {
                    parms.SearchString = string.Format("^{0}$|/{0}/?", searchHost);
                    var listEntries = new PwObjectList<PwEntry>();
                    db.RootGroup.SearchEntries(parms, listEntries);
                    listResult.AddRange(listEntries.Select(x => new PwEntryDatabase(x, db)));
                    if (searchUrls) AddURLCandidates(db, listResult, parms.RespectEntrySearchingDisabled);
                    searchHost = searchHost.Substring(searchHost.IndexOf(".") + 1);

                    //searchHost contains no dot --> prevent possible infinite loop
                    if (searchHost == origSearchHost)
                        break;
                }
                listCount = listResult.Count;
            }

            var filter = new GFunc<PwEntry, bool>((PwEntry e) =>
            {
                var title = e.Strings.ReadSafe(PwDefs.TitleField);
                var entryUrl = e.Strings.ReadSafe(PwDefs.UrlField);
                var c = _ext.GetEntryConfig(e);
                if (c != null)
                {
                    if (configOpt.MatchAuthorityUrl && c.Allow.Equals(formHost))
                        return true;
                    if (c.Allow.Contains(formHost))
                        return true;
                    if (c.Deny.Contains(formHost))
                        return false;
                    if (!string.IsNullOrEmpty(realm) && c.Realm != realm)
                        return false;
                }

                if (IsValidUrl(entryUrl, formHost, configOpt.MatchAuthorityUrl))
                    return true;

                if (IsValidUrl(title, formHost))
                    return true;

                if (searchUrls)
                {
                    foreach (var sf in e.Strings.Where(s => s.Key.StartsWith("URL", StringComparison.InvariantCultureIgnoreCase)))
                    {
                        var sfv = e.Strings.ReadSafe(sf.Key);

                        if (sf.Key.IndexOf("regex", StringComparison.OrdinalIgnoreCase) >= 0
                            && System.Text.RegularExpressions.Regex.IsMatch(formHost, sfv))
                        {
                            return true;
                        }

                        if (IsValidUrl(sfv, formHost, configOpt.MatchAuthorityUrl))
                            return true;
                    }
                }

                return formHost.Contains(title) || (!string.IsNullOrEmpty(entryUrl) && formHost.Contains(entryUrl));
            });

            var filterSchemes = new GFunc<PwEntry, bool>((PwEntry e) =>
            {
                var title = e.Strings.ReadSafe(PwDefs.TitleField);
                var entryUrl = e.Strings.ReadSafe(PwDefs.UrlField);
                Uri entryUri;
                Uri titleUri;

                if (entryUrl != null && Uri.TryCreate(entryUrl, UriKind.Absolute, out entryUri) && entryUri.Scheme == hostUri.Scheme)
                {
                    return true;
                }

                if (Uri.TryCreate(title, UriKind.Absolute, out titleUri) && titleUri.Scheme == hostUri.Scheme)
                {
                    return true;
                }

                return false;
            });

            var result = listResult.Where(e => filter(e.entry));

            if (configOpt.MatchSchemes)
            {
                result = result.Where(e => filterSchemes(e.entry));
            }

            if (configOpt.HideExpired)
            {
                result = result.Where(x => !(x.entry.Expires && x.entry.ExpiryTime <= DateTime.UtcNow));
            }

            return result;
        }

        private void AddURLCandidates(PwDatabase db, List<PwEntryDatabase> listResult, bool bRespectEntrySearchingDisabled)
        {
            var alreadyFound = listResult.Select(x => x.entry);
            var listEntries = db.RootGroup.GetEntries(true)
                .AsEnumerable()
                .Where(x => !alreadyFound.Contains(x));

            if (bRespectEntrySearchingDisabled) listEntries = listEntries.Where(x => x.GetSearchingEnabled());
            foreach (var entry in listEntries)
            {
                if (!entry.Strings.Any(x =>
                    x.Key.StartsWith("URL", StringComparison.InvariantCultureIgnoreCase)
                    && x.Key.ToLowerInvariant().Contains("regex"))) continue;
                listResult.Add(new PwEntryDatabase(entry, db));
            }
        }

        private bool IsValidUrl(string url, string host, bool useAuthority = false)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && _allowedSchemes.Contains(uri.Scheme) && host.EndsWith(useAuthority == true ? uri.Authority : uri.Host);
        }

        private static SearchParameters MakeSearchParameters(bool excludeExpired)
        {
            return new SearchParameters
            {
                SearchInTitles = true,
                SearchInGroupNames = false,
                SearchInNotes = false,
                SearchInOther = true,
                SearchInPasswords = false,
                SearchInTags = false,
                SearchInUrls = true,
                SearchInUserNames = false,
                SearchInUuids = false,
                ExcludeExpired = excludeExpired,
                SearchMode = PwSearchMode.Regular,
                ComparisonMode = StringComparison.InvariantCultureIgnoreCase,
            };
        }
    }
}
