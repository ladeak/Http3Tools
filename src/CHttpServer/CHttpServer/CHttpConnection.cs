using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using CHttpServer.Http3;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal abstract class CHttpConnectionContext
{
    internal required FeatureCollection Features { get; init; }

    internal required long ConnectionId { get; init; }

    internal required CHttpServerOptions ServerOptions { get; set; }

    internal required CancellationTokenSource ConnectionCancellation { get; init; }
}

internal sealed class CHttp2ConnectionContext : CHttpConnectionContext
{
    internal Stream? Transport { get; set; }

    internal IDuplexPipe? TransportPipe { get; set; }
}

internal sealed class CHttp3ConnectionContext : CHttpConnectionContext
{
    internal required QuicConnection Transport { get; init; }
}

internal abstract class CHttpConnection : IThreadPoolWorkItem, IConnectionLifetimeNotificationFeature
{
    private readonly CHttpConnectionContext _connectionContext;
    private readonly ConnectionsManager _connectionsManager;
    private Task? _execution;

    public CHttpConnection(CHttpConnectionContext connectionContext, ConnectionsManager connectionsManager)
    {
        _connectionContext = connectionContext;
        _connectionsManager = connectionsManager;
        _connectionContext.Features.Set<IConnectionLifetimeNotificationFeature>(this);
    }

    public CancellationToken ConnectionClosedRequested { get => _connectionContext.ConnectionCancellation.Token; set => throw new NotSupportedException(); }

    public async Task StopAsync()
    {
        _connectionContext.ConnectionCancellation.Cancel(); // Awaits the execution to complete if there is one.
        var execution = _execution ?? Task.CompletedTask;
        _connectionsManager.RemoveConnection(_connectionContext.ConnectionId);
        await execution.AllowCancellation();
    }

    public void Execute() => _execution = ExecuteAsync();

    public abstract Task ExecuteAsync();

    public void RequestClose()
    {
        _connectionContext.ConnectionCancellation.Cancel();
        _connectionsManager.RemoveConnection(_connectionContext.ConnectionId);
    }

    public virtual void Heartbeat()
    {
    }
}

internal sealed class CHttp2Connection<TContext> : CHttpConnection, IConnectionHeartbeatFeature
    where TContext : notnull
{
    private readonly CHttp2ConnectionContext _connectionContext;
    private readonly Func<CHttp2ConnectionContext, Task> _connectionDelegate;
    private (Action<object> Action, object State)? _heartbeatHandler;

    public CHttp2Connection(CHttp2ConnectionContext connectionContext, ConnectionsManager connectionsManager, Func<CHttp2ConnectionContext, Task> connectionDelegate) : base(connectionContext, connectionsManager)
    {
        _connectionContext = connectionContext;
        _connectionDelegate = connectionDelegate;
        _connectionContext.Features.Set<IConnectionHeartbeatFeature>(this);
    }

    public override void Heartbeat()
    {
        var handler = _heartbeatHandler;
        if (!handler.HasValue)
            return;
        handler.Value.Action(handler.Value.State);
    }

    public void OnHeartbeat(Action<object> action, object state)
    {
        _heartbeatHandler = (action, state);
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

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public override Task ExecuteAsync()
    {
        var connection = new Http3Connection(_connectionContext);
        return connection.ProcessConnectionAsync(_application);
    }
}