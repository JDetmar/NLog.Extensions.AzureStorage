using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureAccessToken.Tests
{
    class AzureServiceTokenProviderMock : AccessTokenLayoutRenderer.IAzureServiceTokenProviderService
    {
        private readonly string _accessToken;
        private readonly TimeSpan _refreshInterval;

        public AzureServiceTokenProviderMock(string accessToken, TimeSpan refreshInterval)
        {
            _accessToken = accessToken;
            _refreshInterval = refreshInterval;
        }

        public async Task<KeyValuePair<string, DateTimeOffset>> GetAuthenticationResultAsync(string resource, string tenantId, CancellationToken cancellationToken)
        {
            await Task.Delay(1);
            return new KeyValuePair<string, DateTimeOffset>(_accessToken ?? Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.Add(_refreshInterval));
        }
    }
}
