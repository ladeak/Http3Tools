using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class InvertBinder : Binder<bool>
{
    private readonly Option<bool> _option;

    public InvertBinder(Option<bool> option)
    {
        _option = option;
    }

    protected override bool GetBoundValue(BindingContext bindingContext)
    {
        var value = bindingContext.ParseResult.GetValueForOption(_option);
        return !value;
    }
}