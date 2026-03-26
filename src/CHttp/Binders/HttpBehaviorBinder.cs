using System.CommandLine;
using CHttp.Http;

namespace CHttp.Binders;

internal sealed class HttpBehaviorBinder(
    Option<bool> redirectBinder,
    Option<bool> enableCertificateValidationBinder,
    Option<double> timeout,
    Option<FileInfo?> cookieContainerOption,
    Option<bool> kerberosAuthOption,
    Option<bool> decompressResponse)
{
    private readonly Option<bool> _redirectBinder = redirectBinder;
    private readonly Option<bool> _enableCertificateValidationBinder = enableCertificateValidationBinder;
    private readonly Option<double> _timeoutOption = timeout;
    private readonly Option<FileInfo?> _cookieContainerOption = cookieContainerOption;
    private readonly Option<bool> _kerberosAuthOption = kerberosAuthOption;
    private readonly Option<bool> _decompressResponse = decompressResponse;

    internal HttpBehavior Bind(ParseResult parseResult)
    {
        var redirects = parseResult.GetValue(_redirectBinder);
        var enableCertificateValidation = parseResult.GetValue(_enableCertificateValidationBinder);
        var timeout = parseResult.GetValue(_timeoutOption);
        var cookieContainer = parseResult.GetValue(_cookieContainerOption)?.FullName ?? string.Empty;
        var kerberosAuth = parseResult.GetValue(_kerberosAuthOption);
        var decompressResponse = parseResult.GetValue(_decompressResponse);
        return new HttpBehavior(timeout, ToUtf8: true, cookieContainer, new SocketBehavior(redirects, enableCertificateValidation, kerberosAuth, 1, decompressResponse));
    }
}
