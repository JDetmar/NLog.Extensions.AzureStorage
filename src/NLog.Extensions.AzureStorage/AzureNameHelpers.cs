
namespace NLog.Extensions.AzureStorage
{
    internal static class AzureNameHelpers
    {
        public static string EnsureValidName(string name, bool ensureToLower = false)
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
    }
}
