using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class HttpRequestDetailsBinder : BinderBase<HttpRequestDetails>
{
    private readonly Binder<HttpMethod> _httpMethodBinder;
    private readonly Binder<Uri> _uriBinder;
    private readonly Binder<Version> _versionBinder;
    private readonly Binder<IEnumerable<KeyValueDescriptor>> _headerBinder;
    private readonly Option<double> _timeoutOption;

    public HttpRequestDetailsBinder(Binder<HttpMethod> httpMethodBinder, Binder<Uri> uriBinder, Binder<Version> versionBinder, Binder<IEnumerable<KeyValueDescriptor>> headerBinder, Option<double> timeout)
    {
        _httpMethodBinder = httpMethodBinder;
        _uriBinder = uriBinder;
        _versionBinder = versionBinder;
        _headerBinder = headerBinder;
        _timeoutOption = timeout;
    }

    protected override HttpRequestDetails GetBoundValue(BindingContext bindingContext)
    {
        var httpMethod = _httpMethodBinder.GetValue(bindingContext);
        var uri = _uriBinder.GetValue(bindingContext);
        var version = _versionBinder.GetValue(bindingContext);
        var headers = _headerBinder.GetValue(bindingContext) ?? Enumerable.Empty<KeyValueDescriptor>();
        var timeout = bindingContext.ParseResult.GetValueForOption<double>(_timeoutOption);

        return new HttpRequestDetails(httpMethod, uri, version, headers, timeout);
    }
}
