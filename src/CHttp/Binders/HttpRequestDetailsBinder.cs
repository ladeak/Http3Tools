using System.CommandLine.Binding;
using CHttp.Data;

namespace CHttp.Binders;

internal sealed class HttpRequestDetailsBinder : BinderBase<HttpRequestDetails>
{
    private readonly Binder<HttpMethod> _httpMethodBinder;
    private readonly Binder<Uri> _uriBinder;
    private readonly Binder<Version> _versionBinder;
    private readonly Binder<IEnumerable<KeyValueDescriptor>> _headerBinder;

    public HttpRequestDetailsBinder(Binder<HttpMethod> httpMethodBinder, Binder<Uri> uriBinder, Binder<Version> versionBinder, Binder<IEnumerable<KeyValueDescriptor>> headerBinder)
    {
        _httpMethodBinder = httpMethodBinder;
        _uriBinder = uriBinder;
        _versionBinder = versionBinder;
        _headerBinder = headerBinder;
    }

    protected override HttpRequestDetails GetBoundValue(BindingContext bindingContext)
    {
        var httpMethod = _httpMethodBinder.GetValue(bindingContext);
        var uri = _uriBinder.GetValue(bindingContext);
        var version = _versionBinder.GetValue(bindingContext);
        var headers = _headerBinder.GetValue(bindingContext) ?? Enumerable.Empty<KeyValueDescriptor>();

        return new HttpRequestDetails(httpMethod, uri, version, headers);
    }
}
