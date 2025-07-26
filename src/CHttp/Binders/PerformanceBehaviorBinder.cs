using System.CommandLine;
using CHttp.Performance.Data;

namespace CHttp.Binders;

internal sealed class PerformanceBehaviorBinder(Option<int> requestsCount, Option<int> clientsCount, Option<bool> sharedSocketsHandler)
{
    private readonly Option<int> _requestsCount = requestsCount;
    private readonly Option<int> _clientsCount = clientsCount;
    private readonly Option<bool> _sharedSocketsHandler = sharedSocketsHandler;

    internal PerformanceBehavior Bind(ParseResult parseResult)
    {
        var requestsCount = parseResult.GetRequiredValue<int>(_requestsCount);
        var clientsCount = parseResult.GetRequiredValue<int>(_clientsCount);
        var sharedSocketsHandler = parseResult.GetRequiredValue<bool>(_sharedSocketsHandler);
        return new PerformanceBehavior(requestsCount, clientsCount, sharedSocketsHandler);
    }
}
