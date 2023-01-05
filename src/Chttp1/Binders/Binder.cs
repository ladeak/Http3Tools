using System.CommandLine.Binding;

namespace CHttp.Binders;

internal abstract class Binder<T> : BinderBase<T>
{
    internal T GetValue(BindingContext bindingContext) => GetBoundValue(bindingContext);
}
