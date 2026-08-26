using System.Net;
using System.Net.Quic;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CHttp.Abstractions;
using CHttp.Binders;
using CHttp.Data;
using CHttp.Http;
using CHttp.Performance;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;
using CHttp.Writers;
using Microsoft.JavaScript.NodeApi;

namespace CHttpExtension;

[JSExport]
public static class CHttpExt
{
    private static CancellationTokenSource _cancellationTokenSource = new();
    private static SemaphoreSlim _semaphore = new SemaphoreSlim(1);
    private static MemoryFileSystem _fileSystem = new MemoryFileSystem();
    private static string _fileNotExistsMessage = "Results for '{0}' are not available";

    public static void SetMsQuicPath(string msquicPath)
    {
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", msquicPath);
        Environment.SetEnvironmentVariable("SSL_CERT_DIR", "/etc/ssl/certs");
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    public static async Task<string> SendRequestAsync(
        bool enableRedirects,
        bool enableCertificateValidation,
        bool useKerberosAuth,
        string? clientCertificatePath,
        string? tlsVersions,
        string? clientCertificateKeyPath,
        double timeout,
        string method,
        string uri,
        string version,
        IEnumerable<string> headers,
        string body
)
    {
        Version parsedVersion = ParseHttpVersion(version);
        _cancellationTokenSource.Cancel();
        if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
            return "Cancelled";
        try
        {
            _cancellationTokenSource = new();
            return await SendRequestImplAsync(enableRedirects, enableCertificateValidation, useKerberosAuth,
                clientCertificatePath, clientCertificateKeyPath, tlsVersions, timeout, method, uri, parsedVersion, headers, body);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task<string> SendRequestImplAsync(
        bool enableRedirects,
        bool enableCertificateValidation,
        bool useKerberosAuth,
        string? clientCertificatePath,
        string? clientCertificateKeyPath,
        string? tlsVersions,
        double timeout,
        string method,
        string uri,
        Version version,
        IEnumerable<string> headers,
        string body
    )
    {
        X509Certificate2? clientCertificate = LoadCertificate(clientCertificatePath, clientCertificateKeyPath);
        var parsedTlsVersions = TlsVersionParser.Map(tlsVersions);
        var httpBehavior = new HttpBehavior(timeout, false, string.Empty,
            new SocketBehavior(enableRedirects, enableCertificateValidation, useKerberosAuth, 1, AutomaticDecompression: true, clientCertificate, parsedTlsVersions));
        var parsedHeaders = new List<KeyValueDescriptor>();
        foreach (string header in headers ?? Enumerable.Empty<string>())
        {
            parsedHeaders.Add(new KeyValueDescriptor(header));
        }
        var requestDetails = new HttpRequestDetails(
            new HttpMethod(method),
            new Uri(uri, UriKind.Absolute),
            version,
            parsedHeaders);
        if (!string.IsNullOrWhiteSpace(body))
            requestDetails = requestDetails with { Content = new StringContent(body) };
        var outputBehavior = new OutputBehavior(LogLevel.Verbose, string.Empty);
        var console = new StringConsole();
        var cookieContainer = new MemoryCookieContainer();
        var writer = new WriterStrategy(outputBehavior, console: console);
        var client = new HttpMessageSender(writer, cookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);
        await client.SendRequestAsync(requestDetails);
        await writer.CompleteAsync(CancellationToken.None);
        await cookieContainer.SaveAsync();

        return console.Text;
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    public static async Task<string> PerfMeasureAsync(
       string executionName,
       bool enableRedirects,
       bool enableCertificateValidation,
       bool useKerberosAuth,
       string? clientCertificatePath,
       string? clientCertificateKeyPath,
       double timeout,
       string method,
       string uri,
       string version,
       IEnumerable<string> headers,
       string body,
       int requestCount,
       int clientsCount,
       bool sharedSocketsHandler,
       string tlsProtocols,
       Action<string> callback
   )
    {
        Version parsedHttpVersion = ParseHttpVersion(version);
        SslProtocols parsedTlsVersions = TlsVersionParser.Map(tlsProtocols);
        _cancellationTokenSource.Cancel();
        if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
            return "Cancelled";
        try
        {
            _cancellationTokenSource = new();
            return await PerfMeasureImplAsync(executionName, enableRedirects, enableCertificateValidation, useKerberosAuth, clientCertificatePath, clientCertificateKeyPath,
                timeout, method, uri, parsedHttpVersion, headers, body, requestCount, clientsCount, sharedSocketsHandler, parsedTlsVersions, callback);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task<string> PerfMeasureImplAsync(
        string? executionName,
        bool enableRedirects,
        bool enableCertificateValidation,
        bool useKerberosAuth,
        string? clientCertificatePath,
        string? clientCertificateKeyPath,
        double timeout,
        string method,
        string uri,
        Version version,
        IEnumerable<string> headers,
        string body,
        int requestCount,
        int clientsCount,
        bool sharedSocketsHandler,
        SslProtocols sslProtocols,
        Action<string> callback
    )
    {
        X509Certificate2? clientCertificate = LoadCertificate(clientCertificatePath, clientCertificateKeyPath);
        var httpBehavior = new HttpBehavior(timeout, false, string.Empty,
            new SocketBehavior(enableRedirects, enableCertificateValidation, useKerberosAuth, 1, AutomaticDecompression: false, clientCertificate, sslProtocols));
        var parsedHeaders = new List<KeyValueDescriptor>();
        foreach (string header in headers ?? Enumerable.Empty<string>())
        {
            parsedHeaders.Add(new KeyValueDescriptor(header));
        }
        var requestDetails = new HttpRequestDetails(
            new HttpMethod(method),
            new Uri(uri, UriKind.Absolute),
            version,
            parsedHeaders);
        if (!string.IsNullOrWhiteSpace(body))
            requestDetails = requestDetails with { Content = new StringContent(body) };

        var performanceBehavior = new PerformanceBehavior(requestCount, clientsCount, sharedSocketsHandler);
        var console = new StringConsole();
        var cookieContainer = new MemoryCookieContainer();
        ISummaryPrinter printer;
        if (string.IsNullOrEmpty(executionName))
            printer = new StatisticsPrinter(console);
        else
            printer = new CompositePrinter(new StatisticsPrinter(console), new FilePrinter(executionName, _fileSystem));
        var orchestrator = new PerformanceMeasureOrchestrator(printer, new StateConsole(callback), new Awaiter(), cookieContainer, new SingleSocketsHandlerProvider(), performanceBehavior);
        await orchestrator.RunAsync(requestDetails, httpBehavior, _cancellationTokenSource.Token);
        return console.Text;
    }

    public static async Task<string> GetDiffAsync(string file1, string file2)
    {
        if (!FilesExist(file1, file2, out var validationMessage))
            throw new ArgumentException(validationMessage);
        var console = new StringConsole(enableColoring: true);
        var session0 = await PerformanceFileHandler.LoadAsync(_fileSystem, file1);
        var session1 = await PerformanceFileHandler.LoadAsync(_fileSystem, file2);
        var comparer = new DiffPrinter(console);
        comparer.Compare(session0, session1);
        return console.Text;
    }


    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    private static Version ParseHttpVersion(string version)
    {
        var parsedVersion = VersionParser.Map(version);
#pragma warning disable CA2252 // This API requires opting into preview features
        if (parsedVersion == HttpVersion.Version30 && !QuicConnection.IsSupported)
            if (Environment.OSVersion.Platform == PlatformID.Unix)
                throw new InvalidOperationException($"QUIC is not supported or not available. Install libmsquic.so from your package registry: https://github.com/ladeak/Http3Tools/issues/68");
            else
                throw new InvalidOperationException($"QUIC is not supported or not available in folder {AppContext.BaseDirectory}.");
#pragma warning restore CA2252 // This API requires opting into preview features
        return parsedVersion;
    }

    private static bool FilesExist(string fileName1, string fileName2, out string error)
    {
        if (!_fileSystem.Exists(fileName1))
        {
            error = string.Format(null, _fileNotExistsMessage, fileName1);
            return false;
        }
        if (!_fileSystem.Exists(fileName2))
        {
            error = string.Format(null, _fileNotExistsMessage, fileName2);
            return false;
        }
        error = string.Empty;
        return true;
    }

    public static void Cancel() => _cancellationTokenSource.Cancel();

    private static X509Certificate2? LoadCertificate(string? certPath, string? certKey)
    {
        if (string.IsNullOrWhiteSpace(certPath) && string.IsNullOrWhiteSpace(certPath))
            return null;

        if (Path.GetExtension(certPath) == ".pfx")
            return X509CertificateLoader.LoadPkcs12FromFile(certPath, certKey);

        var clientCertificate = X509Certificate2.CreateFromPemFile(certPath, certKey);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
#if NET10_0_OR_GREATER
            var exported = clientCertificate.ExportPkcs12(Pkcs12ExportPbeParameters.Default, null);
#else
            var exported = clientCertificate.Export(X509ContentType.Pkcs12);
#endif
            clientCertificate = X509CertificateLoader.LoadPkcs12(exported, null);
        }
        return clientCertificate;
    }
}


