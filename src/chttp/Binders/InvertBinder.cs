using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

public class InvertBinder : BinderBase<bool>
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