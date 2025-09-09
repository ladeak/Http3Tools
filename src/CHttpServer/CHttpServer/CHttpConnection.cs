using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Quic;
using CHttpServer.Http3;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal abstract class CHttpConnectionContext
{
    internal required FeatureCollection Features { get; init; }

    internal required long ConnectionId { get; init; }

    internal required CHttpServerOptions ServerOptions { get; set; }
}

internal class CHttp2ConnectionContext : CHttpConnectionContext
{
    internal Stream? Transport { get; set; }

    internal IDuplexPipe? TransportPipe { get; set; }
}

internal class CHttp3ConnectionContext : CHttpConnectionContext
{
    internal QuicConnection? Transport { get; set; }
}

internal abstract class CHttpConnection : IAsyncDisposable, IThreadPoolWorkItem, IConnectionHeartbeatFeature, IConnectionLifetimeNotificationFeature
{
    private readonly CHttpConnectionContext _connectionContext;
    private readonly ConnectionsManager _connectionsManager;
    private readonly ConcurrentBag<(Action<object> Action, object State)> _heartbeatHandlers;
    private readonly CancellationTokenSource _connectionClosingCts;

    public CHttpConnection(CHttpConnectionContext connectionContext, ConnectionsManager connectionsManager)
    {
        _connectionContext = connectionContext;
        _connectionsManager = connectionsManager;
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

    public abstract Task ExecuteAsync();

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

internal sealed class CHttp2Connection<TContext> : CHttpConnection where TContext : notnull
{
    private readonly CHttp2ConnectionContext _connectionContext;
    private readonly Func<CHttp2ConnectionContext, Task> _connectionDelegate;

    public CHttp2Connection(CHttp2ConnectionContext connectionContext, ConnectionsManager connectionsManager, Func<CHttp2ConnectionContext, Task> connectionDelegate) : base(connectionContext, connectionsManager)
    {
        _connectionContext = connectionContext;
        _connectionDelegate = connectionDelegate;
    }

    public override Task ExecuteAsync() => _connectionDelegate(_connectionContext);
}

internal sealed class CHttp3Connection<TContext> : CHttpConnection where TContext : notnull
{
    private readonly CHttp3ConnectionContext _connectionContext;
    private readonly IHttpApplication<TContext> _application;

    public CHttp3Connection(CHttp3ConnectionContext connectionContext, ConnectionsManager connectionsManager, IHttpApplication<TContext> application) : base(connectionContext, connectionsManager)
    {
        _connectionContext = connectionContext;
        _application = application;
    }

    public override Task ExecuteAsync()
    {
        var connection = new Http3Connection(_connectionContext);
        return connection.ProcessConnectionAsync(_application);
    }
}