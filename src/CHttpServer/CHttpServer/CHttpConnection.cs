using System.IO.Pipelines;
using System.Net.Sockets;

namespace CHttpServer;

public class CHttpConnectionContext
{ 
    internal FeatureCollection Features { get; init;  }

    internal Stream Transport { get; set; }

    internal IDuplexPipe TransportPipe { get; set; }

    internal long ConnectionId { get; set; }
}

public class CHttpConnection : IAsyncDisposable, IThreadPoolWorkItem
{
    private readonly CHttpConnectionContext _connectionContext;
    private readonly ConnectionsManager _connectionsManager;
    private readonly Func<CHttpConnectionContext, Task> _connectionDelegate;

    public CHttpConnection(CHttpConnectionContext connectionContext, ConnectionsManager connectionsManager, Func<CHttpConnectionContext, Task> connectionDelegate)
    {
        _connectionContext = connectionContext;
        _connectionsManager = connectionsManager;
        _connectionDelegate = connectionDelegate;
    }

    public Task AbortAsync()
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _connectionsManager.RemoveConnection(_connectionContext.ConnectionId);
        return ValueTask.CompletedTask;
    }

    public void Execute()
    {
        _ = ExecuteAsync();
    }

    public async Task ExecuteAsync()
    {
        await _connectionDelegate(_connectionContext);
    }

    public void Heartbeat()
    {
        // Manage timeouts
    }

}


public class CHttpConnection<TContext> : CHttpConnection
{
    public CHttpConnection(CHttpConnectionContext connectionContext, ConnectionsManager connectionsManager, Func<CHttpConnectionContext, Task> connectionDelegate) : base(connectionContext, connectionsManager, connectionDelegate)
    {
    }
}