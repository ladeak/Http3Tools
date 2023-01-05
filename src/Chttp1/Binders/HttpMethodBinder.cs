using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class HttpMethodBinder : Binder<HttpMethod>
{
    private readonly Option<string?> _option;

    public HttpMethodBinder(Option<string> option)
    {
        _option = option;
    }

    protected override HttpMethod GetBoundValue(BindingContext bindingContext)
    {
        var value = bindingContext.ParseResult.GetValueForOption(_option) ?? string.Empty;
        return new HttpMethod(value);
    }
}
