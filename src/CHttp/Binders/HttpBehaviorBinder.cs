using System.CommandLine;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CHttp.Http;

namespace CHttp.Binders;

internal sealed class HttpBehaviorBinder(
    Option<bool> redirectBinder,
    Option<bool> validateCertificateValidationBinder,
    Option<double> timeout,
    Option<FileInfo?> cookieContainerOption,
    Option<bool> kerberosAuthOption,
    Option<bool> decompressResponse,
    Option<FileInfo?> clientCertificatePath,
    Option<FileInfo?> clientCertificateKeyPath,
    Option<SslProtocols?> tlsVersion)
{
    private readonly Option<bool> _redirectBinder = redirectBinder;
    private readonly Option<bool> _validateCertificateValidationBinder = validateCertificateValidationBinder;
    private readonly Option<double> _timeoutOption = timeout;
    private readonly Option<FileInfo?> _cookieContainerOption = cookieContainerOption;
    private readonly Option<bool> _kerberosAuthOption = kerberosAuthOption;
    private readonly Option<bool> _decompressResponse = decompressResponse;
    private readonly Option<FileInfo?> _clientCertificatePath = clientCertificatePath;
    private readonly Option<FileInfo?> _clientCertificateKeyPath = clientCertificateKeyPath;
    private readonly Option<SslProtocols?> _tlsVersion = tlsVersion;

    internal HttpBehavior Bind(ParseResult parseResult)
    {
        var redirects = parseResult.GetValue(_redirectBinder);
        var enableCertificateValidation = !parseResult.GetValue(_validateCertificateValidationBinder);
        var timeout = parseResult.GetValue(_timeoutOption);
        var cookieContainer = parseResult.GetValue(_cookieContainerOption)?.FullName ?? string.Empty;
        var kerberosAuth = parseResult.GetValue(_kerberosAuthOption);
        var decompressResponse = parseResult.GetValue(_decompressResponse);
        var clientCertificatePath = parseResult.GetValue(_clientCertificatePath)?.FullName ?? string.Empty;
        var tlsVersion = TlsVersionParser.Map(parseResult.GetValue(_tlsVersion));
        X509Certificate2? clientCertificate = null;

        if (!string.IsNullOrWhiteSpace(clientCertificatePath))
        {
            if (Path.GetExtension(clientCertificatePath) == ".pfx")
                clientCertificate = X509CertificateLoader.LoadPkcs12FromFile(clientCertificatePath, parseResult.GetValue(_clientCertificateKeyPath)?.Name); // Handle KeyPath as a password as opposed file.
            else
            {
                clientCertificate = X509Certificate2.CreateFromPemFile(clientCertificatePath, parseResult.GetValue(_clientCertificateKeyPath)?.FullName);
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
#if NET10_0_OR_GREATER
                    var exported = clientCertificate.ExportPkcs12(Pkcs12ExportPbeParameters.Default, null);
#else
                    var exported = clientCertificate.Export(X509ContentType.Pkcs12);
#endif
                    clientCertificate = X509CertificateLoader.LoadPkcs12(exported, null);
                }
            }
        }

        return new HttpBehavior(timeout, ToUtf8: true, cookieContainer,
            new SocketBehavior(redirects, enableCertificateValidation, kerberosAuth, 1, decompressResponse, clientCertificate, tlsVersion));
    }
}
