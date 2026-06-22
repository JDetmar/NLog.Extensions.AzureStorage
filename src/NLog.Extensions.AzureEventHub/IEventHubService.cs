using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using NLog.Extensions.AzureBlobStorage;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventHubService
    {
        string EventHubName { get; }
        void Connect(string connectionString, string eventHubName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, string eventProducerIdentifier, bool useWebSockets, string endPointAddress, ProxySettings proxySettings);
        Task CloseAsync();
        Task<IEventDataBatch> CreateBatchAsync(string partitionKey, int maxBatchSizeBytes, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Thin abstraction over <c>EventDataBatch</c> so the size-aware splitting in
    /// <see cref="NLog.Targets.EventHubTarget"/> is enforced by the SDK's authoritative
    /// <c>TryAdd</c> overflow check, while still being unit-testable through a fake. The
    /// batch carries the partition key (set via <c>CreateBatchOptions.PartitionKey</c>).
    /// </summary>
    internal interface IEventDataBatch : IDisposable
    {
        /// <summary>Number of events accepted into the batch so far.</summary>
        int Count { get; }

        /// <summary>
        /// Attempts to add an event to the batch. Returns <see langword="false"/> when the
        /// event does not fit: either the batch is full (seal and send it, then retry on a
        /// fresh batch) or - when the batch is empty - the single event is larger than the
        /// maximum allowed size and can never be delivered.
        /// </summary>
        bool TryAddEvent(EventData eventData);

        /// <summary>Sends the events accumulated in this batch.</summary>
        Task SendAsync(CancellationToken cancellationToken);

        /// <summary>The authoritative maximum batch size in bytes enforced by the SDK (the negotiated namespace maximum when no explicit cap was requested).</summary>
        long MaximumSizeInBytes { get; }
    }
}
