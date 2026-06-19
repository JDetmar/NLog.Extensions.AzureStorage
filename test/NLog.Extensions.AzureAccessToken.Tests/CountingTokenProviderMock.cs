using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureAccessToken.Tests
{
    // Counts how often a token is requested, and returns a fixed expiry, so a test
    // can detect a pathological refresh "storm" (timer re-firing every 500ms).
    class CountingTokenProviderMock : AccessTokenLayoutRenderer.IAzureServiceTokenProviderService
    {
        private int _callCount;
        private readonly DateTimeOffset _expiry;

        public int CallCount => Volatile.Read(ref _callCount);

        public CountingTokenProviderMock(DateTimeOffset expiry)
        {
            _expiry = expiry;
        }

        public async Task<KeyValuePair<string, DateTimeOffset>> GetAuthenticationResultAsync(string resource, string tenantId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            return new KeyValuePair<string, DateTimeOffset>("token", _expiry);
        }
    }
}
