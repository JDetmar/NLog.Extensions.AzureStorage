using System.Net;

namespace NLog.Extensions.AzureBlobStorage
{
    /// <summary>
    /// Contains all settings to use to connect through a proxy server
    /// </summary>
    public class ProxySettings
    {
        /// <summary>
        /// proxy server bypass
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
        public bool RequiresManualProxyConfiguration => NoProxy || !string.IsNullOrEmpty(Address) || (!string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Password)) || UseDefaultCredentials;
    }

    /// <summary>
    /// helper class to generate a <see cref="IWebProxy"/> from <see cref="ProxySettings"/>"/>
    /// </summary>
    public class ProxyHelper
    {
        /// <summary>
        /// creates a <see cref="IWebProxy"/> object from the given <see cref="ProxySettings"/>
        /// </summary>
        /// <param name="proxySettings"></param>
        /// <returns></returns>
        public static IWebProxy CreateProxy(ProxySettings proxySettings)
        {
            if (proxySettings.NoProxy)
                return null;
            IWebProxy proxy = WebRequest.DefaultWebProxy;
            if (!string.IsNullOrEmpty(proxySettings.Address))
                proxy = new WebProxy(proxySettings.Address);
            if (!string.IsNullOrEmpty(proxySettings.Login) && !string.IsNullOrEmpty(proxySettings.Password))
                proxy.Credentials = new NetworkCredential(proxySettings.Login, proxySettings.Password);
            return proxy;
        }
    }
}
