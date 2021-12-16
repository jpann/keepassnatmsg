﻿using KeePass.App.Configuration;

namespace KeePassNatMsg
{
    public class ConfigOpt
    {
        readonly AceCustomConfig _config;
        const string ReceiveCredentialNotificationKey = "KeePassHttp_ReceiveCredentialNotification";
        const string SpecificMatchingOnlyKey = "KeePassHttp_SpecificMatchingOnly";
        const string UnlockDatabaseRequestKey = "KeePassHttp_UnlockDatabaseRequest";
        const string AlwaysAllowAccessKey = "KeePassHttp_AlwaysAllowAccess";
        const string AlwaysAllowUpdatesKey = "KeePassHttp_AlwaysAllowUpdates";
        const string SearchInAllOpenedDatabasesKey = "KeePassHttp_SearchInAllOpenedDatabases";
        const string HideExpiredKey = "KeePassHttp_HideExpired";
        const string MatchSchemesKey = "KeePassHttp_MatchSchemes";
        const string ReturnStringFieldsKey = "KeePassHttp_ReturnStringFields";
        const string ReturnStringFieldsWithKphOnlyKey = "KeePassHttp_ReturnStringFieldsWithKphOnly";
        const string SortResultByUsernameKey = "KeePassHttp_SortResultByUsername";
        const string OverrideKeePassXcVersionKey = "KeePassNatMsg_OverrideKeePassXcVersion";
		const string ConnectionDatabaseHashKey = "KeePassHttp_ConnectionDatabaseHash";
        const string SearchUrlsKey = "KeePassHttp_SearchUrls";
        const string UseKeePassXcSettingsKey = "KeePassNatMsg_UseKpxcSettings";
        const string MatchAuthorityUrlKey = "KeePassNatMsg_MatchAuthorityUrl";

        public ConfigOpt(AceCustomConfig config)
        {
            _config = config;
        }

        public bool ReceiveCredentialNotification
        {
            get { return _config.GetBool(ReceiveCredentialNotificationKey, true); }
            set { _config.SetBool(ReceiveCredentialNotificationKey, value); }
        }

        public bool UnlockDatabaseRequest
        {
            get { return _config.GetBool(UnlockDatabaseRequestKey, false); }
            set { _config.SetBool(UnlockDatabaseRequestKey, value); }
        }

        public bool SpecificMatchingOnly
        {
            get { return _config.GetBool(SpecificMatchingOnlyKey, false); }
            set { _config.SetBool(SpecificMatchingOnlyKey, value); }
        }

        public bool AlwaysAllowAccess
        {
            get { return _config.GetBool(AlwaysAllowAccessKey, false); }
            set { _config.SetBool(AlwaysAllowAccessKey, value); }
        }

        public bool AlwaysAllowUpdates
        {
            get { return _config.GetBool(AlwaysAllowUpdatesKey, false); }
            set { _config.SetBool(AlwaysAllowUpdatesKey, value); }
        }

        public bool SearchInAllOpenedDatabases
        {
            get { return _config.GetBool(SearchInAllOpenedDatabasesKey, false); }
            set { _config.SetBool(SearchInAllOpenedDatabasesKey, value); }
        }

        public bool HideExpired
        {
            get { return _config.GetBool(HideExpiredKey, false); }
            set { _config.SetBool(HideExpiredKey, value); }
        }
        public bool MatchSchemes
        {
            get { return _config.GetBool(MatchSchemesKey, false); }
            set { _config.SetBool(MatchSchemesKey, value); }
        }

        public bool ReturnStringFields
        {
            get { return _config.GetBool(ReturnStringFieldsKey, false); }
            set { _config.SetBool(ReturnStringFieldsKey, value); }
        }

        public bool ReturnStringFieldsWithKphOnly
        {
            get { return _config.GetBool(ReturnStringFieldsWithKphOnlyKey, true); }
            set { _config.SetBool(ReturnStringFieldsWithKphOnlyKey, value); }
        }

        public bool SortResultByUsername
        {
            get { return _config.GetBool(SortResultByUsernameKey, true); }
            set { _config.SetBool(SortResultByUsernameKey, value); }
        }

        public string OverrideKeePassXcVersion
        {
            get
            {
                return _config.GetString(OverrideKeePassXcVersionKey);
            }
            set
            {
                _config.SetString(OverrideKeePassXcVersionKey, value);
            }
        }

        public string ConnectionDatabaseHash
		{
			get { return _config.GetString(ConnectionDatabaseHashKey, string.Empty); }
			set { _config.SetString(ConnectionDatabaseHashKey, value); }
		}

        public bool SearchUrls
        {
            get
            {
                return _config.GetBool(SearchUrlsKey, false);
            }
            set
            {
                _config.SetBool(SearchUrlsKey, value);
            }
        }

        public bool UseKeePassXcSettings
        {
            get
            {
                return _config.GetBool(UseKeePassXcSettingsKey, false);
            }
            set
            {
                _config.SetBool(UseKeePassXcSettingsKey, value);
            }
        }

        public bool MatchAuthorityUrl
        {
            get { return _config.GetBool(MatchAuthorityUrlKey, false); }
            set { _config.SetBool(MatchAuthorityUrlKey, value); }
        }
    }
}
