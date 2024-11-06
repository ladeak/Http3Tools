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
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
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

        var feature = new CHttpTlsConnectionFeature(sslStream);
        context.Features.Set<ITlsConnectionFeature>(feature);
        context.Features.Set<ITlsHandshakeFeature>(feature);
        context.Features.Set<ITlsApplicationProtocolFeature>(feature);
        context.Features.Set<ISslStreamFeature>(feature);

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
        catch (IOException e)
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
        var sslOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = _serverCertificate,
            ServerCertificateContext = _serverCertificateContext,
            ServerCertificateSelectionCallback = null,
            ClientCertificateRequired = _options.ClientCertificateMode == ClientCertificateMode.AllowCertificate
                || _options.ClientCertificateMode == ClientCertificateMode.RequireCertificate,
            EnabledSslProtocols = _options.SslProtocols,
            CertificateRevocationCheckMode = _options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
        };

        ConfigureAlpn(sslOptions);
        return sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);
    }


    private bool RemoteCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        return RemoteCertificateValidationCallback(_options.ClientCertificateMode, _options.ClientCertificateValidation, certificate, chain, sslPolicyErrors);
    }

    private DuplexPipeStreamAdapter<SslStream> CreateSslDuplexPipe(Stream transport, MemoryPool<byte> memoryPool)
    {
        StreamPipeReaderOptions inputPipeOptions = new StreamPipeReaderOptions
        (
            pool: memoryPool,
            bufferSize: 8192 * 2,
            minimumReadSize: 8192,
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


internal sealed class CHttpTlsConnectionFeature : ITlsConnectionFeature, ITlsApplicationProtocolFeature, ITlsHandshakeFeature, ISslStreamFeature
{
    private readonly SslStream _sslStream;
    private X509Certificate2? _clientCert;

    public CHttpTlsConnectionFeature(SslStream sslStream)
    {
        ArgumentNullException.ThrowIfNull(sslStream);
        _sslStream = sslStream;
    }

    internal bool AllowDelayedClientCertificateNegotation { get; set; }

    public X509Certificate2? ClientCertificate
    {
        get
        {
            return _clientCert ??= ConvertToX509Certificate2(_sslStream.RemoteCertificate);
        }
        set
        {
            _clientCert = value;
        }
    }

    public string HostName { get; set; } = string.Empty;

    public ReadOnlyMemory<byte> ApplicationProtocol => _sslStream.NegotiatedApplicationProtocol.Protocol;

    public SslProtocols Protocol => _sslStream.SslProtocol;

    public SslStream SslStream => _sslStream;

    // We don't store the values for these because they could be changed by a renegotiation.

    public TlsCipherSuite? NegotiatedCipherSuite => _sslStream.NegotiatedCipherSuite;

    public CipherAlgorithmType CipherAlgorithm => _sslStream.CipherAlgorithm;

    public int CipherStrength => _sslStream.CipherStrength;

    public HashAlgorithmType HashAlgorithm => _sslStream.HashAlgorithm;

    public int HashStrength => _sslStream.HashStrength;

    public ExchangeAlgorithmType KeyExchangeAlgorithm => _sslStream.KeyExchangeAlgorithm;

    public int KeyExchangeStrength => _sslStream.KeyExchangeStrength;

    public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken)
    {
        if (ClientCertificate != null
            || !AllowDelayedClientCertificateNegotation
            || _sslStream.NegotiatedApplicationProtocol == SslApplicationProtocol.Http2)
        {
            return Task.FromResult(ClientCertificate);
        }

        return GetClientCertificateAsyncCore(cancellationToken);
    }

    private async Task<X509Certificate2?> GetClientCertificateAsyncCore(CancellationToken cancellationToken)
    {
        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            await _sslStream.NegotiateClientCertificateAsync(cancellationToken);
#pragma warning restore CA1416 // Validate platform compatibility
        }
        catch (PlatformNotSupportedException)
        {
            // NegotiateClientCertificateAsync might not be supported on all platforms.
            // Don't attempt to recover by creating a new connection. Instead, just throw error directly to the app.
            throw;
        }
        return ClientCertificate;
    }

    private static X509Certificate2? ConvertToX509Certificate2(X509Certificate? certificate)
    {
        return certificate switch
        {
            null => null,
            X509Certificate2 cert2 => cert2,
            _ => new X509Certificate2(certificate),
        };
    }
}
