using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Azure.Services.AppAuthentication;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.LayoutRenderers;

namespace NLog.Extensions.AzureAccessToken
{
    [LayoutRenderer("AzureAccessToken")]
    [ThreadAgnostic]
    [ThreadSafe]
    public class AccessTokenLayoutRenderer :  LayoutRenderer
    {
        internal static readonly ConcurrentDictionary<TokenProviderKey, WeakReference<AccessTokenRefresher>> AccessTokenProviders = new ConcurrentDictionary<TokenProviderKey, WeakReference<AccessTokenRefresher>>();
        private readonly Func<string, string, IAzureServiceTokenProviderService> _tokenProviderFactory;
        private AccessTokenRefresher _tokenRefresher;

        /// <summary>
        /// Ex. https://management.azure.com or https://database.windows.net/ or https://storage.azure.com/
        /// </summary>
        [RequiredParameter]
        public Layout ResourceName { get; set; }

        /// <summary>
        /// TenantId (or directory Id) of your Azure Active Directory. Ex hosttenant.onmicrosoft.com
        /// </summary>
        public Layout TenantId { get; set; }

        public Layout ConnectionString { get; set; }

        public Layout AzureAdInstance { get; set; }

        public AccessTokenLayoutRenderer()
            :this((connectionString,azureAdInstance) => new AzureServiceTokenProviderService(connectionString, azureAdInstance))
        {
        }

        internal AccessTokenLayoutRenderer(Func<string, string, IAzureServiceTokenProviderService> tokenProviderFactory)
        {
            _tokenProviderFactory = tokenProviderFactory;
        }

        protected override void InitializeLayoutRenderer()
        {
            ResetTokenRefresher();
            LookupAccessTokenRefresher();
            base.InitializeLayoutRenderer();
        }

        protected override void CloseLayoutRenderer()
        {
            ResetTokenRefresher();
            base.CloseLayoutRenderer();
        }

        internal void ResetTokenRefresher()
        {
            var tokenRefresher = Interlocked.Exchange(ref _tokenRefresher, null);
            if (tokenRefresher != null)
                tokenRefresher.AccessTokenRefreshed -= AccessTokenRefreshed;
        }

        private void AccessTokenRefreshed(object sender, EventArgs eventArgs)
        {
            InternalLogger.Debug("AccessToken LayoutRenderer - AccessToken Refreshed");
        }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var tokenProvider = LookupAccessTokenRefresher();
            builder.Append(tokenProvider?.AccessToken);
        }

        AccessTokenRefresher LookupAccessTokenRefresher()
        {
            if (_tokenRefresher != null)
                return _tokenRefresher;

            var resourceName = ResourceName?.Render(LogEventInfo.CreateNullEvent());
            if (string.IsNullOrEmpty(resourceName))
            {
                InternalLogger.Warn("AccessToken LayoutRenderer - Missing ResourceName");
                return null;
            }

            var tenantId = TenantId?.Render(LogEventInfo.CreateNullEvent());
            if (string.IsNullOrWhiteSpace(tenantId))
                tenantId = null;
            var connectionString = ConnectionString?.Render(LogEventInfo.CreateNullEvent());
            var azureAdInstance = AzureAdInstance?.Render(LogEventInfo.CreateNullEvent());

            var tokenProviderKey = new TokenProviderKey(resourceName, tenantId, connectionString, azureAdInstance);
            AccessTokenRefresher tokenRefresher = null;
            if (!AccessTokenProviders.TryGetValue(tokenProviderKey, out var tokenProvider) || !tokenProvider.TryGetTarget(out tokenRefresher))
            {
                var serviceProvider = _tokenProviderFactory(connectionString, azureAdInstance);
                lock (AccessTokenProviders)
                {
                    if (_tokenRefresher == null)
                    {
                        if (!AccessTokenProviders.TryGetValue(tokenProviderKey, out tokenProvider) || !tokenProvider.TryGetTarget(out tokenRefresher))
                        {
                            tokenRefresher = new AccessTokenRefresher(serviceProvider, resourceName, tenantId);
                            AccessTokenProviders[tokenProviderKey] = new WeakReference<AccessTokenRefresher>(tokenRefresher);
                        }
                    }
                }
            }

            if (Interlocked.CompareExchange(ref _tokenRefresher, tokenRefresher, null) == null)
            {
                _tokenRefresher.AccessTokenRefreshed += AccessTokenRefreshed;
            }

            return _tokenRefresher;
        }

        internal struct TokenProviderKey : IEquatable<TokenProviderKey>
        {
            public readonly string ResourceName;
            public readonly string TenantId;
            public readonly string ConnectionString;
            public readonly string AzureAdInstance;

            public TokenProviderKey(string resourceName, string tenantId, string connectionString, string azureAdInstance)
            {
                ResourceName = resourceName ?? string.Empty;
                TenantId = tenantId ?? string.Empty;
                ConnectionString = connectionString ?? string.Empty;
                AzureAdInstance = azureAdInstance ?? string.Empty;
            }

            public bool Equals(TokenProviderKey other)
            {
                return string.Equals(ResourceName, other.ResourceName)
                    && string.Equals(TenantId, other.TenantId)
                    && string.Equals(ConnectionString, other.ConnectionString)
                    && string.Equals(AzureAdInstance, other.AzureAdInstance);
            }

            public override bool Equals(object obj)
            {
                return obj is TokenProviderKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 23 + ResourceName.GetHashCode();
                hash = hash * 23 + TenantId.GetHashCode();
                hash = hash * 23 + ConnectionString.GetHashCode();
                hash = hash * 23 + AzureAdInstance.GetHashCode();
                return hash;
            }
        }

        internal sealed class AccessTokenRefresher : IDisposable
        {
            private readonly object _lockObject = new object();
            private readonly IAzureServiceTokenProviderService _tokenProvider;
            private CancellationTokenSource _cancellationTokenSource;
            private readonly Timer _refreshTimer;
            private readonly string _resource;
            private readonly string _tenantId;

            public AccessTokenRefresher(IAzureServiceTokenProviderService tokenProvider, string resource, string tenantId)
            {
                _tokenProvider = tokenProvider;
                _cancellationTokenSource = new CancellationTokenSource();
                _resource = resource;
                _tenantId = tenantId;
                _refreshTimer = new Timer(TimerRefresh, this, Timeout.Infinite, Timeout.Infinite);
            }

            public string AccessToken => WaitForToken();
            private string _accessToken;

            private static void TimerRefresh(object state)
            {
                _ = ((AccessTokenRefresher)state).RefreshToken();
            }

            private string WaitForToken()
            {
                if (_accessToken == null)
                {
                    for (int i = 0; i < 100; ++i)
                    {
                        Task.Delay(1).GetAwaiter().GetResult();
                        var accessToken = Interlocked.CompareExchange(ref _accessToken, null, null);
                        if (accessToken != null)
                            return accessToken;
                    }
                    Interlocked.CompareExchange(ref _accessToken, string.Empty, null);

                    if (string.IsNullOrEmpty(_accessToken))
                        InternalLogger.Warn("AccessToken LayoutRenderer - No AccessToken received from AzureServiceTokenProvider");
                }
                else
                {
                    if (string.IsNullOrEmpty(_accessToken))
                        InternalLogger.Debug("AccessToken LayoutRenderer - Missing AccessToken from AzureServiceTokenProvider");
                }

                return _accessToken;
            }

            private async Task RefreshToken()
            {
                TimeSpan nextRefresh = TimeSpan.Zero;

                try
                {
                    var authResult = await _tokenProvider.GetAuthenticationResultAsync(_resource, _tenantId, _cancellationTokenSource.Token).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(_accessToken))
                        InternalLogger.Debug("AccessToken LayoutRenderer - Acquired {0} AccessToken from AzureServiceTokenProvider", string.IsNullOrEmpty(authResult.Key) ? "Empty" : "Valid");

                    Interlocked.Exchange(ref _accessToken, authResult.Key);

                    if (_accessTokenRefreshed != null)
                    {
                        lock (_lockObject)
                            _accessTokenRefreshed?.Invoke(this, EventArgs.Empty);
                    }

                    // Renew the token 5 minutes before it expires.
                    nextRefresh = (authResult.Value - DateTimeOffset.UtcNow) - TimeSpan.FromMinutes(5);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AccessToken LayoutRenderer - Failed getting AccessToken from AzureServiceTokenProvider");
                }
                finally
                {
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        if (nextRefresh < TimeSpan.FromMilliseconds(500))
                            nextRefresh = TimeSpan.FromMilliseconds(500);
                        _refreshTimer.Change((int)nextRefresh.TotalMilliseconds, Timeout.Infinite);
                    }
                }
            }

            public void Dispose()
            {
                _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _cancellationTokenSource.Cancel();
                _refreshTimer.Dispose();
                _cancellationTokenSource.Dispose();
                _accessTokenRefreshed = null;
            }

            public event EventHandler<EventArgs> AccessTokenRefreshed
            {
                add
                {
                    lock (_lockObject)
                    {
                        var wasCancelled = _accessTokenRefreshed == null;
                        _accessTokenRefreshed += value;
                        if (wasCancelled)
                        {
                            _cancellationTokenSource = new CancellationTokenSource();
                            _refreshTimer.Change(1, Timeout.Infinite);
                        }
                    }
                }
                remove
                {
                    lock (_lockObject)
                    {
                        _accessTokenRefreshed -= value;
                        if (_accessTokenRefreshed == null)
                        {
                            _cancellationTokenSource.Cancel();
                            _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        }
                    }
                }
            }

            private event EventHandler<EventArgs> _accessTokenRefreshed;
        }

        internal interface IAzureServiceTokenProviderService
        {
            Task<KeyValuePair<string, DateTimeOffset>> GetAuthenticationResultAsync(string resource, string tenantId, CancellationToken cancellationToken);
        }

        private class AzureServiceTokenProviderService : IAzureServiceTokenProviderService
        {
            private readonly AzureServiceTokenProvider _tokenProvider;

            public AzureServiceTokenProviderService(string connectionString, string azureAdInstance)
            {
                _tokenProvider = string.IsNullOrEmpty(azureAdInstance) ? new AzureServiceTokenProvider(connectionString) : new AzureServiceTokenProvider(connectionString, azureAdInstance);
            }

            public async Task<KeyValuePair<string, DateTimeOffset>> GetAuthenticationResultAsync(string resource, string tenantId, CancellationToken cancellationToken)
            {
                var result = await _tokenProvider.GetAuthenticationResultAsync(resource, tenantId, cancellationToken).ConfigureAwait(false);
                return new KeyValuePair<string, DateTimeOffset>(result?.AccessToken, result?.ExpiresOn ?? default(DateTimeOffset));
            }
        }
    }
}
