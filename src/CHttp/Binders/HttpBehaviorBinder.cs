using System.CommandLine;
using CHttp.Http;

namespace CHttp.Binders;

internal sealed class HttpBehaviorBinder
{
    private readonly Option<bool> _redirectBinder;
    private readonly Option<bool> _enableCertificateValidationBinder;
    private readonly Option<double> _timeoutOption;
    private readonly Option<FileInfo?> _cookieContainerOption;
    private readonly Option<bool> _kerberosAuthOption;

    public HttpBehaviorBinder(
        Option<bool> redirectBinder,
        Option<bool> enableCertificateValidationBinder,
        Option<double> timeout,
        Option<FileInfo?> cookieContainerOption,
        Option<bool> kerberosAuthOption)
    {
        _redirectBinder = redirectBinder;
        _enableCertificateValidationBinder = enableCertificateValidationBinder;
        _timeoutOption = timeout;
        _cookieContainerOption = cookieContainerOption;
        _kerberosAuthOption = kerberosAuthOption;

    }

    internal HttpBehavior Bind(ParseResult parseResult)
    {
        var redirects = parseResult.GetValue(_redirectBinder);
        var enableCertificateValidation = parseResult.GetValue(_enableCertificateValidationBinder);
        var timeout = parseResult.GetValue(_timeoutOption);
        var cookieContainer = parseResult.GetValue(_cookieContainerOption)?.Name ?? string.Empty;
        var kerberosAuth = parseResult.GetValue(_kerberosAuthOption);
        return new HttpBehavior(timeout, ToUtf8: true, cookieContainer, new SocketBehavior(redirects, enableCertificateValidation, kerberosAuth, 1));
    }
}
