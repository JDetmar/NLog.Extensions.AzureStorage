namespace NLog.Extensions.AzureStorage
{
    internal static class AzureCredentialHelpers
    {
        internal static Azure.Identity.DefaultAzureCredential CreateTokenCredentials(string managedIdentityClientId, string tenantIdentity, string managedIdentityResourceId)
        {
            var options = new Azure.Identity.DefaultAzureCredentialOptions();

            if (!string.IsNullOrWhiteSpace(tenantIdentity))
            {
                options.TenantId = tenantIdentity;
            }

            if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
            {
                options.ManagedIdentityClientId = managedIdentityClientId;
                options.WorkloadIdentityClientId = managedIdentityClientId;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(managedIdentityResourceId))
                {
                    options.ManagedIdentityResourceId = new Azure.Core.ResourceIdentifier(managedIdentityResourceId);
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