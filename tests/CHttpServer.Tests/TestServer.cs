using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace CHttpServer.Tests;

public class TestServer : IAsyncDisposable, IDisposable
{
    private WebApplication? _app;

    public Task RunAsync(int port = 7222, bool usePriority = false)
    {
        if (_app != null)
            return Task.CompletedTask;
        var builder = WebApplication.CreateBuilder();
        builder.UseCHttpServer(o =>
        {
            o.Port = port;
            o.Certificate = X509CertificateLoader.LoadPkcs12FromFile("testCert.pfx", "testPassword");
            o.UsePriority = usePriority;
        });

        // Use Kestrel:
        //builder.WebHost.UseKestrel(o =>
        //{
        //    o.Listen(IPAddress.Loopback, 7222, lo =>
        //    {
        //        lo.UseHttps();
        //        lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
        //    });
        //});
        _app = builder.Build();
        _app.MapGet("/nostatuscode", () =>
        {
            return "TypedResults.NoContent()";
        });
        _app.MapGet("/nocontent", () =>
        {
            return TypedResults.NoContent();
        });
        _app.MapGet("/content", () =>
        {
            return TypedResults.Ok("some content");
        });
        _app.MapPost("/post", ([FromBody] WeatherForecast body) =>
        {
            return TypedResults.NoContent();
        });
        _app.MapGet("/httpcontext", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("some content");
        });
        _app.MapGet("/stream", async (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("some content");
            await Task.Delay(1000);
            await ctx.Response.WriteAsync("some content2");
        });
        _app.MapGet("/iasyncenumerable", (HttpContext ctx) =>
        {
            async IAsyncEnumerable<string> GetStream()
            {
                foreach (var i in Enumerable.Range(0, 2))
                {
                    await Task.Delay(1000);
                    yield return "some content";
                }
            }
            return GetStream();
        });
        _app.MapGet("/headerstrailers", async (HttpContext ctx) =>
        {
            if (!ctx.Request.Headers.Accept.Contains("application/json") || ctx.Request.Headers["x-custom"] != "custom-header-value")
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            ctx.Response.Headers.TryAdd("x-custom-response", "custom-header-value");
            ctx.Response.Headers.ContentType = "application/json";
            ctx.Response.DeclareTrailer("x-trailer");
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("some content");
            ctx.Response.AppendTrailer("x-trailer", new Microsoft.Extensions.Primitives.StringValues("mytrailer"));

        });
        _app.MapGet("/headersecho", async (HttpContext ctx) =>
        {
            foreach (var header in ctx.Request.Headers)
                if (header.Key.StartsWith("x-custom"))
                    ctx.Response.Headers.TryAdd(header.Key, header.Value);
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("some content");

        });
        _app.MapPost("/readallrequest", async (HttpContext ctx) =>
        {
            var ms = new MemoryStream();
            await ctx.Request.BodyReader.CopyToAsync(ms);
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync(ms.Length.ToString());
        });
        _app.MapGet("/getlargeresponse", async (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.BodyWriter.WriteAsync(new byte[10_000_000]);
        });
        _app.MapGet("/getlargestreamresponse", async (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            for (int i = 0; i < 100; i++)
            {
                await ctx.Response.BodyWriter.WriteAsync(new byte[100_000]);
                await ctx.Response.BodyWriter.FlushAsync();
            }
        });
        _app.MapGet("/responsePriority", (HttpContext ctx) =>
        {
            ctx.Features.Get<IPriority9218Feature>()?.SetPriority(new Priority9218(1, true));
            return TypedResults.NoContent();
        });
        return _app.RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app == null)
            return;
        await _app.StopAsync();
        await _app.WaitForShutdownAsync();
    }

    public void Dispose()
    {
        DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }
}
