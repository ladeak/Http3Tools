using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace CHttpServer;

internal sealed class HttpsConnectionMiddleware
{
    private readonly Func<CHttpConnectionContext, Task> _next;
    private readonly TimeSpan _handshakeTimeout;
    private readonly HttpsConnectionAdapterOptions _options;
    private readonly Func<Stream, SslStream> _sslStreamFactory;

    private readonly SslStreamCertificateContext? _serverCertificateContext;
    private readonly X509Certificate2? _serverCertificate;
    private readonly Func<CHttpConnectionContext, string?, X509Certificate2?>? _serverCertificateSelector;

    public HttpsConnectionMiddleware(Func<CHttpConnectionContext,Task> next, HttpsConnectionAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        _handshakeTimeout = options.HandshakeTimeout;
        _options = options;

        // capture the certificate now so it can't be switched after validation
        _serverCertificate = options.ServerCertificate;
        //_serverCertificateSelector = options.ServerCertificateSelector;

        // If a selector is provided then ignore the cert, it may be a default cert.
        if (_serverCertificateSelector != null)
        {
            // SslStream doesn't allow both.
            _serverCertificate = null;
        }
        else
        {
            Debug.Assert(_serverCertificate != null);
            var certificate = _serverCertificate;
            if (!certificate.HasPrivateKey)
            {
                // SslStream historically has logic to deal with certificate missing private keys.
                // By resolving the SslStreamCertificateContext eagerly, we circumvent this logic so
                // try to resolve the certificate from the store if there's no private key in the cert.
                certificate = LocateCertificateWithPrivateKey(certificate);
            }

            // This might be do blocking IO but it'll resolve the certificate chain up front before any connections are
            // made to the server
            _serverCertificateContext = SslStreamCertificateContext.Create(certificate, additionalCertificates: options.ServerCertificateChain);
        }

        var remoteCertificateValidationCallback = _options.ClientCertificateMode == ClientCertificateMode.NoCertificate ?
            (RemoteCertificateValidationCallback?)null : RemoteCertificateValidationCallback;

        _sslStreamFactory = s => new SslStream(s, leaveInnerStreamOpen: false, userCertificateValidationCallback: remoteCertificateValidationCallback);
    }

    public async Task OnConnectionAsync(CHttpConnectionContext context)
    {
        if (context.Features.Get<ITlsConnectionFeature>() != null)
        {
            await _next(context);
            return;
        }

        var sslDuplexPipe = CreateSslDuplexPipe(
            context.Transport,
            context.Features.Get<IMemoryPoolFeature>()?.MemoryPool ?? MemoryPool<byte>.Shared);
        var sslStream = sslDuplexPipe.Stream;

        //var feature = new TlsConnectionFeature(sslStream, context);
        //context.Features.Set<ITlsConnectionFeature>(feature);
        //context.Features.Set<ITlsHandshakeFeature>(feature);
        //context.Features.Set<ITlsApplicationProtocolFeature>(feature);
        //context.Features.Set<ISslStreamFeature>(feature);

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(_handshakeTimeout);

            await DoOptionsBasedHandshakeAsync(context, sslStream, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            await sslStream.DisposeAsync();
            return;
        }
        catch (IOException)
        {
            await sslStream.DisposeAsync();
            return;
        }
        catch (AuthenticationException)
        {
            await sslStream.DisposeAsync();
            return;
        }

        var originalTransport = context.Transport;

        try
        {
            context.Transport = sslDuplexPipe;
            context.TransportPipe = sslDuplexPipe;

            // Disposing the stream will dispose the sslDuplexPipe
            await using (sslStream)
            await using (sslDuplexPipe)
            {
                await _next(context);
                // Dispose the inner stream (SslDuplexPipe) before disposing the SslStream
                // as the duplex pipe can hit an ODE as it still may be writing.
            }
        }
        finally
        {
            // Restore the original so that it gets closed appropriately
            context.Transport = originalTransport;
        }
    }

    private X509Certificate2 LocateCertificateWithPrivateKey(X509Certificate2 certificate)
    {
        Debug.Assert(!certificate.HasPrivateKey, "This should only be called with certificates that don't have a private key");
        X509Store? OpenStore(StoreLocation storeLocation)
        {
            try
            {
                var store = new X509Store(StoreName.My, storeLocation);
                store.Open(OpenFlags.ReadOnly);
                return store;
            }
            catch (Exception exception) when (exception is CryptographicException || exception is SecurityException)
            {
                return null;
            }
        }

        try
        {
            var store = OpenStore(StoreLocation.LocalMachine);

            if (store != null)
            {
                using (store)
                {
                    var certs = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);

                    if (certs.Count > 0 && certs[0].HasPrivateKey)
                    {
                        return certs[0];
                    }
                }
            }

            store = OpenStore(StoreLocation.CurrentUser);

            if (store != null)
            {
                using (store)
                {
                    var certs = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);

                    if (certs.Count > 0 && certs[0].HasPrivateKey)
                    {
                        return certs[0];
                    }
                }
            }
        }
        catch (CryptographicException)
        {
            throw;
        }

        // Return the cert, and it will fail later
        return certificate;
    }

    internal static void ConfigureAlpn(SslServerAuthenticationOptions serverOptions)
    {
        serverOptions.ApplicationProtocols = [SslApplicationProtocol.Http2];
        serverOptions.AllowRenegotiation = false;
    }

    internal static bool RemoteCertificateValidationCallback(
        ClientCertificateMode clientCertificateMode,
        Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>? clientCertificateValidation,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate == null)
        {
            return clientCertificateMode != ClientCertificateMode.RequireCertificate;
        }

        if (clientCertificateValidation == null)
        {
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                return false;
            }
        }

        var certificate2 = ConvertToX509Certificate2(certificate);
        if (certificate2 == null)
        {
            return false;
        }

        if (clientCertificateValidation != null)
        {
            if (!clientCertificateValidation(certificate2, chain, sslPolicyErrors))
            {
                return false;
            }
        }

        return true;
    }

    private Task DoOptionsBasedHandshakeAsync(CHttpConnectionContext context, SslStream sslStream, CancellationToken cancellationToken)
    {
        Debug.Assert(_options != null, "Middleware must be created with options.");

        // Adapt to the SslStream signature
        ServerCertificateSelectionCallback? selector = null;
        if (_serverCertificateSelector != null)
        {
            selector = (sender, name) =>
            {
                //feature.HostName = name ?? string.Empty;
                var cert = _serverCertificateSelector(context, name);
                return cert!;
            };
        }

        var sslOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = _serverCertificate,
            ServerCertificateContext = _serverCertificateContext,
            ServerCertificateSelectionCallback = selector,
            ClientCertificateRequired = _options.ClientCertificateMode == ClientCertificateMode.AllowCertificate
                || _options.ClientCertificateMode == ClientCertificateMode.RequireCertificate,
            EnabledSslProtocols = _options.SslProtocols,
            CertificateRevocationCheckMode = _options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
        };

        ConfigureAlpn(sslOptions);

        //_options.OnAuthenticate?.Invoke(context, sslOptions);

        return sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);
    }


    private bool RemoteCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        Debug.Assert(_options != null, "Middleware must be created with options.");

        return RemoteCertificateValidationCallback(_options.ClientCertificateMode, _options.ClientCertificateValidation, certificate, chain, sslPolicyErrors);
    }

    private DuplexPipeStreamAdapter<SslStream> CreateSslDuplexPipe(Stream transport, MemoryPool<byte> memoryPool)
    {
        StreamPipeReaderOptions inputPipeOptions = new StreamPipeReaderOptions
        (
            pool: memoryPool,
            bufferSize: 4096 * 2,
            minimumReadSize: 4096,
            leaveOpen: true,
            useZeroByteReads: true
        );

        var outputPipeOptions = new StreamPipeWriterOptions
        (
            pool: memoryPool,
            leaveOpen: true
        );
        var sslStream = _sslStreamFactory(transport);
        return new DuplexPipeStreamAdapter<SslStream>(sslStream, inputPipeOptions, outputPipeOptions);
    }

    private static X509Certificate2? ConvertToX509Certificate2(X509Certificate? certificate)
    {
        if (certificate == null)
        {
            return null;
        }

        if (certificate is X509Certificate2 cert2)
        {
            return cert2;
        }

        return new X509Certificate2(certificate);
    }
}

internal sealed class HttpConnection<TContext> where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;

    public HttpConnection(IHttpApplication<TContext> application)
    {
        _application = application;
    }

    public Task OnConnectionAsync(CHttpConnectionContext connectionContext)
    {
        throw new NotImplementedException();
        //var memoryPoolFeature = connectionContext.Features.Get<IMemoryPoolFeature>();
        //var protocols = connectionContext.Features.Get<HttpProtocolsFeature>()?.HttpProtocols ?? _endpointDefaultProtocols;
        //var metricContext = connectionContext.Features.GetRequiredFeature<IConnectionMetricsContextFeature>().MetricsContext;
        //var localEndPoint = connectionContext.LocalEndPoint as IPEndPoint;

        //var httpConnectionContext = new HttpConnectionContext(
        //    connectionContext.ConnectionId,
        //    protocols,
        //    altSvcHeader,
        //    connectionContext,
        //    _serviceContext,
        //    connectionContext.Features,
        //    memoryPoolFeature?.MemoryPool ?? System.Buffers.MemoryPool<byte>.Shared,
        //    localEndPoint,
        //    connectionContext.RemoteEndPoint as IPEndPoint,
        //    metricContext);
        //httpConnectionContext.Transport = connectionContext.Transport;

        //var connection = new HttpConnection(httpConnectionContext);

        //return connection.ProcessRequestsAsync(_application);
    }
}