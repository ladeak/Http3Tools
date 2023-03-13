using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class HttpBehaviorBinder : BinderBase<HttpBehavior>
{
    private readonly Binder<bool> _redirectBinder;
    private readonly Binder<bool> _enableCertificateValidationBinder;

    public HttpBehaviorBinder(Binder<bool> redirectBinder, Binder<bool> enableCertificateValidationBinder)
    {
        _redirectBinder = redirectBinder;
        _enableCertificateValidationBinder = enableCertificateValidationBinder;
    }

    protected override HttpBehavior GetBoundValue(BindingContext bindingContext)
    {
        var redirects = _redirectBinder.GetValue(bindingContext);
        var enableCertificateValidation = _enableCertificateValidationBinder.GetValue(bindingContext);
        return new HttpBehavior(redirects, enableCertificateValidation);
    }
}
