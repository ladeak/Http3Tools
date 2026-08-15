using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace CHttp.Http;

internal abstract class BaseSocketsHandlerProvider
{
    public abstract SocketsHttpHandler GetMessageHandler(ICookieContainer cookieContainer, SocketBehavior behavior);

    protected SocketsHttpHandler CreateMessageHandler(ICookieContainer cookieContainer, SocketBehavior behavior)
    {
        var messageHandler = new SocketsHttpHandler();
        messageHandler.MaxConnectionsPerServer = behavior.MaxConnectionPerServer;
        messageHandler.AllowAutoRedirect = behavior.EnableRedirects;
        messageHandler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions()
        {
            // Sockets behavior
            CertificateRevocationCheckMode = X509RevocationMode.Offline,
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
        };
        if (behavior.ClientCertificate != null)
            messageHandler.SslOptions.ClientCertificates = [behavior.ClientCertificate];

        if (!behavior.EnableCertificateValidation)
            messageHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        if (behavior.UseKerberosAuth)
        {
            messageHandler.DefaultProxyCredentials = CredentialCache.DefaultNetworkCredentials;
            messageHandler.Credentials = CredentialCache.DefaultCredentials;
        }
        if (behavior.AutomaticDecompression)
            messageHandler.AutomaticDecompression = DecompressionMethods.All;

        messageHandler.UseCookies = true;
        messageHandler.CookieContainer = cookieContainer.Load();
        messageHandler.MaxResponseHeadersLength = 1024;
        return messageHandler;
    }
}


internal sealed class SharedSocketsHandlerProvider : BaseSocketsHandlerProvider
{
    private SocketsHttpHandler? _handler;

    public override SocketsHttpHandler GetMessageHandler(ICookieContainer cookieContainer, SocketBehavior behavior)
    {
        if (_handler != null)
            return _handler;

        Interlocked.CompareExchange(ref _handler, CreateMessageHandler(cookieContainer, behavior), null);
        return _handler;
    }
}

internal sealed class SingleSocketsHandlerProvider : BaseSocketsHandlerProvider
{
    public override SocketsHttpHandler GetMessageHandler(ICookieContainer cookieContainer, SocketBehavior behavior) => CreateMessageHandler(cookieContainer, behavior);
}