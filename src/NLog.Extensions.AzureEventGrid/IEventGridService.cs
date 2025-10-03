using Azure.Messaging;
using Azure.Messaging.EventGrid;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventGridService
    {
        string Topic { get; }

        void Connect(string topic, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string accessKey, string clientAuthId, string clientAuthSecret);

        Task SendEventAsync(EventGridEvent gridEvent, CancellationToken cancellationToken);

        Task SendEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken);
    }
}
