using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CHttpServer;

public static class HostExtensions
{
    public static IWebHostBuilder UseCHttpServer(this IWebHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.TryAddSingleton<IConnectionListenerFactory, SocketTransportFactory>();
            //services.AddSingleton<IHttpsConfigurationService, HttpsConfigurationService>();
            //services.AddSingleton<HttpsConfigurationService.IInitializer, HttpsConfigurationService.Initializer>();
            //services.AddSingleton<IServer, KestrelServerImpl>();
        });
        return hostBuilder;
    }
}
