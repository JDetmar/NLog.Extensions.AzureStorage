using Azure.Core.Pipeline;
using System.Net;
using System.Net.Http;

namespace NLog.Extensions.AzureBlobStorage
{
    /// <summary>
    /// Contains all settings to use to connect through a proxy server
    /// </summary>
    public class ProxySettings
    {
        /// <summary>
        /// type of proxy to use
        /// </summary>
        public ProxyType ProxyType { get; set; } = ProxyType.Default;

        /// <summary>
        /// address of the proxy server (including port number)
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// login to the proxy server
        /// </summary>
        public string Login { get; set; }

        /// <summary>
        /// Password to use for the proxy server.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        ///  If this value is set to true, the default credentials for the proxy
        /// </summary>
        public bool UseDefaultCredentials { get; set; }

        /// <summary>
        /// Determines whether a custom proxy is required for the given settings
        /// </summary>
        /// <returns><see langword="true"/> if a custom proxy is required; otherwise, <see langword="false"/>.</returns>
        private bool RequiresManualProxyConfiguration => (ProxyType == ProxyType.Default && !string.IsNullOrEmpty(Address)) || ProxyType == ProxyType.NoProxy || (ProxyType == ProxyType.SystemProxy && !string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Password)) || UseDefaultCredentials;

        /// <summary>
        /// creates a custom HttpPipelineTransport to be used as <see cref="Azure.Core.ClientOptions.Transport"/> for storage targets
        /// </summary>
        /// <returns></returns>
        public HttpClientTransport CreateHttpClientTransport()
        {
            if (RequiresManualProxyConfiguration)
            {
                var handler = new HttpClientHandler
                {
                    UseProxy = ProxyType != ProxyType.NoProxy,
                    Proxy = ProxyType == ProxyType.Default && !string.IsNullOrEmpty(Address) ? CreateProxy(this) : null
                };
                if (handler.Proxy == null && handler.UseProxy) // using default proxy
                {
                    if (UseDefaultCredentials)
                        handler.DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials;
                    else if (!string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Password))
                        handler.DefaultProxyCredentials = new System.Net.NetworkCredential(Login, Password);
                }
                return new HttpClientTransport(new HttpClient(handler));
            }

            return null;
        }

        private static WebProxy CreateProxy(ProxySettings proxySettings)
        {
            var proxy = new WebProxy(proxySettings.Address);
            if (!string.IsNullOrEmpty(proxySettings.Login) && !string.IsNullOrEmpty(proxySettings.Password))
                proxy.Credentials = new NetworkCredential(proxySettings.Login, proxySettings.Password);
            return proxy;
        }
    }

    /// <summary>
    /// defines which proxy type to use
    /// </summary>
    public enum ProxyType 
    {
        /// <summary>
        /// Do not change the proxy settings (default)
        /// </summary>
        Default, 
        /// <summary>
        /// Explicitly disables proxy
        /// </summary>
        NoProxy,
        /// <summary>
        /// Use the ssystem-wide proxy settings (on Windows: Control Panel -> Internet Options)
        /// </summary>
        SystemProxy
    }
}
