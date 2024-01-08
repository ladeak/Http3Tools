using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class HttpBehaviorBinder : BinderBase<HttpBehavior>
{
    private readonly Binder<bool> _redirectBinder;
    private readonly Binder<bool> _enableCertificateValidationBinder;
    private readonly Option<double> _timeoutOption;
    private readonly Option<string> _cookieContainerOption;
    private readonly Option<bool> _kerberosAuthOption;

    public HttpBehaviorBinder(
        Binder<bool> redirectBinder,
        Binder<bool> enableCertificateValidationBinder,
        Option<double> timeout,
        Option<string> cookieContainerOption,
        Option<bool> kerberosAuthOption)
    {
        _redirectBinder = redirectBinder;
        _enableCertificateValidationBinder = enableCertificateValidationBinder;
        _timeoutOption = timeout;
        _cookieContainerOption = cookieContainerOption;
        _kerberosAuthOption = kerberosAuthOption;

    }

    protected override HttpBehavior GetBoundValue(BindingContext bindingContext)
    {
        var redirects = _redirectBinder.GetValue(bindingContext);
        var enableCertificateValidation = _enableCertificateValidationBinder.GetValue(bindingContext);
        var timeout = bindingContext.ParseResult.GetValueForOption<double>(_timeoutOption);
        var cookieContainer = bindingContext.ParseResult.GetValueForOption<string>(_cookieContainerOption) ?? string.Empty;
        var kerberosAuth = bindingContext.ParseResult.GetValueForOption<bool>(_kerberosAuthOption);
        return new HttpBehavior(timeout, ToUtf8: true, cookieContainer, new SocketBehavior(redirects, enableCertificateValidation, kerberosAuth, 1));
    }
}
