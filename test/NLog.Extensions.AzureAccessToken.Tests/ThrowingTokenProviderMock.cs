using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureAccessToken.Tests
{
    // Always fails token acquisition, and counts attempts, so a test can detect a
    // pathological retry "storm" (timer re-firing every 500ms) after a failure.
    class ThrowingTokenProviderMock : AccessTokenLayoutRenderer.IAzureServiceTokenProviderService
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<KeyValuePair<string, DateTimeOffset>> GetAuthenticationResultAsync(string resource, string tenantId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("simulated token acquisition failure");
        }
    }
}
