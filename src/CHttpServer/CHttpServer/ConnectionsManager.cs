using System.Collections.Concurrent;

namespace CHttpServer;

internal sealed class ConnectionsManager
{
    private readonly ConcurrentDictionary<long, CHttpConnection> _connections = new ConcurrentDictionary<long, CHttpConnection>();

    private long _connectionId = 1;

    public long GetNewConnectionId() => _connectionId++;

    public void AddConnection(long id, CHttpConnection connection)
    {
        if (!_connections.TryAdd(id, connection))
            throw new ArgumentException("Unable to add specified id.", nameof(id));
    }

    public void RemoveConnection(long id)
    {
        if (!_connections.Remove(id, out _))
            throw new ArgumentException("Unable to remove specified id.", nameof(id));
    }

    public Task StopAsync()
    {
        List<Task> connectionAbortions = new List<Task>();
        foreach (var connection in _connections)
            connectionAbortions.Add(connection.Value.StopAsync());
        return Task.WhenAll(connectionAbortions);
    }

    public async Task RunHeartbeat(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
                foreach (var connection in _connections)
                {
                    token.ThrowIfCancellationRequested();
                    connection.Value.Heartbeat();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}