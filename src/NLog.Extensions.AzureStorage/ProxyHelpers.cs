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
        /// bypasses any proxy server that may have been configured
        /// </summary>
        public bool NoProxy { get; set; }

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
        public bool RequiresManualProxyConfiguration => !string.IsNullOrEmpty(Address) || NoProxy || (!string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Password)) || UseDefaultCredentials;

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
                    UseProxy = !NoProxy,
                };

                if (NoProxy)
                {
                    handler.Proxy = null;
                }
                else if (!string.IsNullOrEmpty(Address))
                {
                    handler.Proxy = CreateWebProxy();
                }
                else
                {
                    if (UseDefaultCredentials)
                        handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
                    else if (!string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Password))
                        handler.DefaultProxyCredentials = new NetworkCredential(Login, Password);
                }

                return new HttpClientTransport(new HttpClient(handler));
            }

            return null;
        }

        /// <summary>
        /// Creates new WebProxy-object based on the configured proxy-options.
        /// </summary>
        public IWebProxy CreateWebProxy(IWebProxy defaultProxy = null)
        {
            if (NoProxy)
                return null;
            if (string.IsNullOrEmpty(Address))
                return defaultProxy;

            var proxy = new WebProxy(Address, BypassOnLocal: true);
            if (UseDefaultCredentials)
                proxy.Credentials = CredentialCache.DefaultCredentials;
            else if (!string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Password))
                proxy.Credentials = new NetworkCredential(Login, Password);
            return proxy;
        }
    }
}
