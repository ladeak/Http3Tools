using System.Net;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CHttpServer;

public class CHttpServerOptions
{
    public int? Port { get; set; }

    public IPAddress? Host { get; set; }
}

public static class HostExtensions
{
    public static IWebHostBuilder UseCHttpServer(this IWebHostBuilder hostBuilder, Action<CHttpServerOptions> configure)
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.TryAddSingleton<IConnectionListenerFactory, SocketTransportFactory>();
            //services.AddSingleton<IHttpsConfigurationService, HttpsConfigurationService>();
            //services.AddSingleton<HttpsConfigurationService.IInitializer, HttpsConfigurationService.Initializer>();
            services.Configure<CHttpServerOptions>(configure);
            services.AddSingleton<IServer, CHttpServerImpl>();
        });
        return hostBuilder;
    }
}
