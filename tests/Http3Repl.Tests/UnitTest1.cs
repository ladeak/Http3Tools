using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;

namespace Http3Repl.Tests;

[Collection(nameof(NonParallel))]
public class UnitTest1
{
    [QuicSupported]
    public async Task Test1()
    {
        using var app = CreateHostApplication();
        await app.StartAsync();
    }

    private IHost CreateHostApplication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.ListenLocalhost(5011, options =>
            {
                options.UseHttps();
                options.Protocols = HttpProtocols.Http3;
            });
        });
        var app = builder.Build();

        app.MapGet("/", async context =>
        {
            await context.Response.WriteAsync("Hello World " + context.Request.Protocol.ToString());
        });

        return app;
    }
}