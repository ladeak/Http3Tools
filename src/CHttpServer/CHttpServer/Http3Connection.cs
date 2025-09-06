using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal sealed partial class Http3Connection
{
    private CHttp3ConnectionContext _context;

    public Http3Connection(CHttp3ConnectionContext connectionContext)
    {
        _context = connectionContext;
    }

    internal Task ProcessRequestAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        return Task.CompletedTask;
    }
}