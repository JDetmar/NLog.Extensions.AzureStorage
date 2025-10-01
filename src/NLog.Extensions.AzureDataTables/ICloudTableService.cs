using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using NLog.Extensions.AzureBlobStorage;

namespace NLog.Extensions.AzureStorage
{
    interface ICloudTableService
    {
        void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, ProxySettings proxySettings = null);
        Task SubmitTransactionAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken);
    }
}
