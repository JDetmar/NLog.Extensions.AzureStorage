namespace NLog.Extensions.AzureStorage
{
    internal static class AzureCredentialHelpers
    {
        internal static Azure.Identity.DefaultAzureCredential CreateTokenCredentials(string clientIdentity, string tenantIdentity, string resourceIdentifier)
        {
            var options = new Azure.Identity.DefaultAzureCredentialOptions();

            if (!string.IsNullOrWhiteSpace(tenantIdentity))
            {
                options.TenantId = tenantIdentity;
            }

            if (!string.IsNullOrWhiteSpace(clientIdentity))
            {
                options.ManagedIdentityClientId = clientIdentity;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(resourceIdentifier))
                {
                    options.ManagedIdentityResourceId = new Azure.Core.ResourceIdentifier(resourceIdentifier);
                }
                else if (string.IsNullOrWhiteSpace(tenantIdentity))
                {
                    return new Azure.Identity.DefaultAzureCredential(); // Default Azure Credential
                }
            }

            return new Azure.Identity.DefaultAzureCredential(options);
        }
    }
}