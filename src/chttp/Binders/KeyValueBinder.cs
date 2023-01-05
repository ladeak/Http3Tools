using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class KeyValueBinder : Binder<IEnumerable<KeyValueDescriptor>>
{
    private readonly Option<IEnumerable<string>> _headers;

    public KeyValueBinder(Option<IEnumerable<string>> headers)
    {
        _headers = headers;
    }

    protected override IEnumerable<KeyValueDescriptor> GetBoundValue(BindingContext bindingContext)
    {
        var values = bindingContext.ParseResult.GetValueForOption(_headers);
        foreach (string header in values ?? Enumerable.Empty<string>())
        {
            yield return new KeyValueDescriptor(header);
        }

    }
}