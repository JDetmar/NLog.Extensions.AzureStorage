using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NLog.Extensions.AzureStorage
{
    internal sealed class AzureStorageNameCache
    {
        private readonly Dictionary<string, string> _storageNameCache = new Dictionary<string, string>();

        public string LookupStorageName(string requestedName, Func<string, string> checkAndRepairName)
        {
            if (_storageNameCache.TryGetValue(requestedName, out var validName))
                return validName;

            if (_storageNameCache.Count > 1000)
                _storageNameCache.Clear();

            validName = checkAndRepairName(requestedName);
            _storageNameCache[requestedName] = validName;
            return validName;
        }

        private static string EnsureValidName(string name, bool ensureToLower = false)
        {
            if (name?.Length > 0)
            {
                if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[name.Length - 1]))
                    name = name.Trim();

                for (int i = 0; i < name.Length; ++i)
                {
                    char chr = name[i];
                    if (chr >= 'A' && chr <= 'Z')
                    {
                        if (ensureToLower)
                            name = name.ToLowerInvariant();
                        continue;
                    }
                    if (chr >= 'a' && chr <= 'z')
                        continue;
                    if (i != 0 && chr >= '0' && chr <= '9')
                        continue;

                    return null;
                }

                return name;
            }

            return null;
        }

        /// <summary>
        /// Checks the and repairs container name acording to the Azure naming rules.
        /// </summary>
        /// <param name="requestedContainerName">Name of the requested container.</param>
        /// <returns></returns>
        public static string CheckAndRepairContainerNamingRules(string requestedContainerName)
        {
            /*  https://docs.microsoft.com/en-us/rest/api/storageservices/fileservices/naming-and-referencing-containers--blobs--and-metadata
            Container Names
                A container name must be a valid DNS name, conforming to the following naming rules:
                Container names must start with a letter or number, and can contain only letters, numbers, and the dash (-) character.
                Every dash (-) character must be immediately preceded and followed by a letter or number,
                consecutive dashes are not permitted in container names.
                All letters in a container name must be lowercase.
                Container names must be from 3 through 63 characters long.
            */
            var simpleValidName = requestedContainerName?.Length <= 63 ? EnsureValidName(requestedContainerName, ensureToLower: true) : null;
            if (simpleValidName?.Length >= 3)
                return simpleValidName;

            requestedContainerName = requestedContainerName?.Trim() ?? string.Empty;
            const string validContainerPattern = "^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$";
            var loweredRequestedContainerName = requestedContainerName.ToLower();
            if (Regex.Match(loweredRequestedContainerName, validContainerPattern, RegexOptions.ExplicitCapture).Success)
            {
                //valid name okay to lower and use
                return loweredRequestedContainerName;
            }

            const string trimLeadingPattern = "^.*?(?=[a-zA-Z0-9])";
            const string trimTrailingPattern = "(?<=[a-zA-Z0-9]).*?";
            const string trimFobiddenCharactersPattern = "[^a-zA-Z0-9-]";
            const string trimExtraHyphensPattern = "-+";

            requestedContainerName = requestedContainerName.Replace('.', '-').Replace('_', '-').Replace('\\', '-').Replace('/', '-').Replace(' ', '-').Replace("--", "-").Replace("--", "-").Trim(new[] { '-' });
            var pass1 = Regex.Replace(requestedContainerName, trimFobiddenCharactersPattern, String.Empty, RegexOptions.ExplicitCapture);
            var pass2 = Regex.Replace(pass1, trimTrailingPattern, String.Empty, RegexOptions.RightToLeft | RegexOptions.ExplicitCapture);
            var pass3 = Regex.Replace(pass2, trimLeadingPattern, String.Empty, RegexOptions.ExplicitCapture);
            var pass4 = Regex.Replace(pass3, trimExtraHyphensPattern, "-", RegexOptions.ExplicitCapture);
            var loweredCleanedContainerName = pass4.ToLower();
            if (Regex.Match(loweredCleanedContainerName, validContainerPattern, RegexOptions.ExplicitCapture).Success)
            {
                return loweredCleanedContainerName;
            }
            return "defaultlog";
        }

        /// <summary>
        /// Checks the and repairs table name acording to the Azure naming rules.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <returns></returns>
        public static string CheckAndRepairTableNamingRules(string tableName)
        {
            /*  http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
            Table Names:
                Table names must be unique within an account.
                Table names may contain only alphanumeric characters.
                Table names cannot begin with a numeric character.
                Table names are case-insensitive.
                Table names must be from 3 to 63 characters long.
                Some table names are reserved, including "tables". Attempting to create a table with a reserved table name returns error code 404 (Bad Request).
            */
            var simpleValidName = tableName?.Length <= 63 ? EnsureValidName(tableName) : null;
            if (simpleValidName?.Length >= 3)
                return simpleValidName;

            const string trimLeadingPattern = "^.*?(?=[a-zA-Z])";
            const string trimFobiddenCharactersPattern = "[^a-zA-Z0-9-]";

            var pass1 = Regex.Replace(tableName, trimFobiddenCharactersPattern, String.Empty, RegexOptions.ExplicitCapture);
            var cleanedTableName = Regex.Replace(pass1, trimLeadingPattern, String.Empty, RegexOptions.ExplicitCapture);
            if (String.IsNullOrWhiteSpace(cleanedTableName) || cleanedTableName.Length > 63 || cleanedTableName.Length < 3)
            {
                var tableDefault = "Logs";
                return tableDefault;
            }
            return tableName;
        }
    }
}
