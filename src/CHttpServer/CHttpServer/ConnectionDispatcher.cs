using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal sealed class ConnectionDispatcher<TContext> where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;

    public ConnectionDispatcher(IHttpApplication<TContext> application)
    {
        _application = application;
    }

    public Task OnConnectionAsync(CHttpConnectionContext connectionContext)
    {
        var connection = new Http2Connection(connectionContext);
        return connection.ProcessRequestAsync(_application);
    }
}
