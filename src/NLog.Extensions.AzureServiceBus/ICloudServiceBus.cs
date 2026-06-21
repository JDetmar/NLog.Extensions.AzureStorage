using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using NLog.Extensions.AzureBlobStorage;

namespace NLog.Extensions.AzureStorage
{
    internal interface ICloudServiceBus
    {
        string EntityPath { get; }
        TimeSpan? DefaultTimeToLive { get; }
        void Connect(string connectionString, string queueOrTopicName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, string eventProducerIdentifier, bool useWebSockets, string endPointAddress, TimeSpan? timeToLive, ProxySettings proxySettings);
        Task<IServiceBusMessageBatch> CreateMessageBatchAsync(int maxBatchSizeBytes, CancellationToken cancellationToken);
        Task CloseAsync();
    }

    /// <summary>
    /// Thin abstraction over <c>ServiceBusMessageBatch</c> so the size-aware splitting in
    /// <see cref="NLog.Targets.ServiceBusTarget"/> is enforced by the SDK's authoritative
    /// <c>TryAddMessage</c> overflow check, while still being unit-testable through a fake.
    /// </summary>
    internal interface IServiceBusMessageBatch : IDisposable
    {
        /// <summary>Number of messages accepted into the batch so far.</summary>
        int Count { get; }

        /// <summary>
        /// Attempts to add a message to the batch. Returns <see langword="false"/> when the
        /// message does not fit: either the batch is full (seal and send it, then retry on a
        /// fresh batch) or - when the batch is empty - the single message is larger than the
        /// maximum allowed size and can never be delivered.
        /// </summary>
        bool TryAddMessage(ServiceBusMessage message);

        /// <summary>Sends the messages accumulated in this batch.</summary>
        Task SendAsync(CancellationToken cancellationToken);
    }
}
