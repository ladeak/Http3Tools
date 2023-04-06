using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class PerformanceBehaviorBinder : BinderBase<PerformanceBehavior>
{
    private readonly Option<int> _requestsCount;
    private readonly Option<int> _clientsCount;

    public PerformanceBehaviorBinder(Option<int> requestsCount, Option<int> clientsCount)
    {
        _requestsCount = requestsCount;
        _clientsCount = clientsCount;
    }

    protected override PerformanceBehavior GetBoundValue(BindingContext bindingContext)
    {
        var requestsCount = bindingContext.ParseResult.GetValueForOption<int>(_requestsCount);
        var clientsCount = bindingContext.ParseResult.GetValueForOption<int>(_clientsCount);

        return new PerformanceBehavior(requestsCount, clientsCount);
    }
}
