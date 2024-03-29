﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace NLog.Extensions.AzureStorage
{
    interface ICloudTableService
    {
        void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentifier, string clientIdentity, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey);
        Task SubmitTransactionAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken);
    }
}
