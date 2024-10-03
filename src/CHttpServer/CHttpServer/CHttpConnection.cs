using System.Collections.Concurrent;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Connections.Features;

namespace CHttpServer;

internal class CHttpConnectionContext
{
    internal required FeatureCollection Features { get; init; }

    internal Stream? Transport { get; set; }

    internal IDuplexPipe? TransportPipe { get; set; }

    internal required long ConnectionId { get; init; }

    internal required CHttpServerOptions ServerOptions { get; set; }
}

internal class CHttpConnection : IAsyncDisposable, IThreadPoolWorkItem, IConnectionHeartbeatFeature, IConnectionLifetimeNotificationFeature
{
    private readonly CHttpConnectionContext _connectionContext;
    private readonly ConnectionsManager _connectionsManager;
    private readonly Func<CHttpConnectionContext, Task> _connectionDelegate;
    private readonly ConcurrentBag<(Action<object> Action, object State)> _heartbeatHandlers;
    private readonly CancellationTokenSource _connectionClosingCts;

    public CHttpConnection(CHttpConnectionContext connectionContext, ConnectionsManager connectionsManager, Func<CHttpConnectionContext, Task> connectionDelegate)
    {
        _connectionContext = connectionContext;
        _connectionsManager = connectionsManager;
        _connectionDelegate = connectionDelegate;
        _heartbeatHandlers = new();
        _connectionClosingCts = new();
        _connectionContext.Features.Set<IConnectionHeartbeatFeature>(this);
        _connectionContext.Features.Set<IConnectionLifetimeNotificationFeature>(this);
    }

    public CancellationToken ConnectionClosedRequested { get => _connectionClosingCts.Token; set => throw new NotSupportedException(); }

    public Task AbortAsync()
    {
        _connectionClosingCts.Cancel();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _connectionClosingCts.Dispose();
        _connectionsManager.RemoveConnection(_connectionContext.ConnectionId);
        return ValueTask.CompletedTask;
    }

    public void Execute()
    {
        _ = ExecuteAsync();
    }

    public Task ExecuteAsync() => _connectionDelegate(_connectionContext);

    public void Heartbeat()
    {
        foreach (var handler in _heartbeatHandlers)
            handler.Action(handler.State);
    }

    public void OnHeartbeat(Action<object> action, object state)
    {
        _heartbeatHandlers.Add((action, state));
    }

    public void RequestClose()
    {
        _connectionClosingCts.Cancel();
    }
}

internal sealed class CHttpConnection<TContext> : CHttpConnection
{
    public CHttpConnection(CHttpConnectionContext connectionContext, ConnectionsManager connectionsManager, Func<CHttpConnectionContext, Task> connectionDelegate) : base(connectionContext, connectionsManager, connectionDelegate)
    {
    }
}