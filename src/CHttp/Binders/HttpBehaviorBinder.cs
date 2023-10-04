using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class HttpBehaviorBinder : BinderBase<HttpBehavior>
{
	private readonly Binder<bool> _redirectBinder;
	private readonly Binder<bool> _enableCertificateValidationBinder;
	private readonly Option<double> _timeoutOption;
	private readonly Option<string> _cookieContainerOption;

	public HttpBehaviorBinder(
		Binder<bool> redirectBinder,
		Binder<bool> enableCertificateValidationBinder,
		Option<double> timeout,
		Option<string> cookieContainerOption)
	{
		_redirectBinder = redirectBinder;
		_enableCertificateValidationBinder = enableCertificateValidationBinder;
		_timeoutOption = timeout;
		_cookieContainerOption = cookieContainerOption;
	}

	protected override HttpBehavior GetBoundValue(BindingContext bindingContext)
	{
		var redirects = _redirectBinder.GetValue(bindingContext);
		var enableCertificateValidation = _enableCertificateValidationBinder.GetValue(bindingContext);
		var timeout = bindingContext.ParseResult.GetValueForOption<double>(_timeoutOption);
		var cookieContainer = bindingContext.ParseResult.GetValueForOption<string>(_cookieContainerOption) ?? string.Empty;

		return new HttpBehavior(redirects, enableCertificateValidation, timeout, true, cookieContainer);
	}
}
