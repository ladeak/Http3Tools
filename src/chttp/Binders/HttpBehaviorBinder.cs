using System.CommandLine;
using System.CommandLine.Binding;
using CHttp.Data;

namespace CHttp.Binders;

internal sealed class HttpBehaviorBinder : BinderBase<HttpBehavior>
{
    private readonly Binder<bool> _redirectBinder;
    private readonly Binder<bool> _enableCertificateValidationBinder;
    private readonly Option<LogLevel> _logLevelOption;

    public HttpBehaviorBinder(Binder<bool> redirectBinder, Binder<bool> enableCertificateValidationBinder, Option<LogLevel> logLevelOption)
    {
        _redirectBinder = redirectBinder;
        _enableCertificateValidationBinder = enableCertificateValidationBinder;
        _logLevelOption = logLevelOption;
    }

    protected override HttpBehavior GetBoundValue(BindingContext bindingContext)
    {
        var redirects = _redirectBinder.GetValue(bindingContext);
        var enableCertificateValidation = _enableCertificateValidationBinder.GetValue(bindingContext);
        var logLevel = bindingContext.ParseResult.GetValueForOption(_logLevelOption);

        return new HttpBehavior(redirects, enableCertificateValidation, logLevel);
    }
}
