using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CHttp.Tests;

internal static class HttpServer
{
    public static WebApplication CreateHostBuilder(RequestDelegate requestDelegate, HttpProtocols? protocol = null, Action<KestrelServerOptions>? configureKestrel = null, Action<IServiceCollection>? configureServices = null, int port = 5011)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel =>
        {
            if (configureKestrel == null)
            {
                kestrel.ListenAnyIP(port, options =>
                {
                    options.UseHttps(new X509Certificate2("testCert.pfx", "testPassword"));
                    options.Protocols = protocol ?? HttpProtocols.Http3;
                });
            }
            else
            {
                configureKestrel(kestrel);
            }
        });

        configureServices?.Invoke(builder.Services);
        var app = builder.Build();

        app.Map("/", requestDelegate);
        return app;
    }
}