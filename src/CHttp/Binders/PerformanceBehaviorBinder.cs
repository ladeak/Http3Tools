using System.CommandLine;
using System.CommandLine.Binding;

namespace CHttp.Binders;

internal sealed class PerformanceBehaviorBinder(Option<int> requestsCount, Option<int> clientsCount, Option<bool> sharedSocketsHandler) : BinderBase<PerformanceBehavior>
{
    private readonly Option<int> _requestsCount = requestsCount;
    private readonly Option<int> _clientsCount = clientsCount;
    private readonly Option<bool> _sharedSocketsHandler = sharedSocketsHandler;

    protected override PerformanceBehavior GetBoundValue(BindingContext bindingContext)
    {
        var requestsCount = bindingContext.ParseResult.GetValueForOption<int>(_requestsCount);
        var clientsCount = bindingContext.ParseResult.GetValueForOption<int>(_clientsCount);
        var sharedSocketsHandler = bindingContext.ParseResult.GetValueForOption<bool>(_sharedSocketsHandler);

        return new PerformanceBehavior(requestsCount, clientsCount, sharedSocketsHandler);
    }
}
