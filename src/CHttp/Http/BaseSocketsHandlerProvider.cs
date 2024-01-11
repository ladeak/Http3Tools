using System.Net;

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
            // TODO: sockets behavior
            CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Offline,
        };

        if (!behavior.EnableCertificateValidation)
            messageHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        if (behavior.UseKerberosAuth)
        {
            messageHandler.DefaultProxyCredentials = CredentialCache.DefaultNetworkCredentials;
            messageHandler.Credentials = CredentialCache.DefaultCredentials;
        }

        messageHandler.UseCookies = true;
        messageHandler.CookieContainer = cookieContainer.Load();
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