using System;
using System.Collections.Generic;
using System.Text;
using Azure.Messaging.EventGrid;
using System.Threading.Tasks;
using Azure.Messaging;
using System.Threading;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventGridService
    {
        string Topic { get; }

        void Connect(string topic, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string accessKey);

        Task SendEventAsync(EventGridEvent gridEvent, CancellationToken cancellationToken);

        Task SendEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken);
    }
}
