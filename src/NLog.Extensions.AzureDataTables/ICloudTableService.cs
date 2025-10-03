using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace NLog.Extensions.AzureStorage
{
    interface ICloudTableService
    {
        void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret);
        Task SubmitTransactionAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken);
    }
}
