using System.Threading;
using System.Threading.Tasks;
#if NETSTANDARD2_0 || NET461
using Microsoft.Azure.Cosmos.Table;
#else
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
#endif

namespace NLog.Extensions.AzureStorage
{
    interface ICloudTableService
    {
        void Connect(string connectionString, int? defaultTimeToLiveSeconds);
        Task ExecuteBatchAsync(string tableName, TableBatchOperation tableOperation, CancellationToken cancellationToken);
    }
}
