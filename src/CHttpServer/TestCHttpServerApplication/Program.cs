using System.Text.Json.Serialization;
using CHttpServer;

var builder = WebApplication.CreateBuilder(args);
builder.UseCHttpServer();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
var app = builder.Build();

app.MapGet("/", async context =>
{
    context.Response.Headers.TryAdd("x-custom", new Microsoft.Extensions.Primitives.StringValues("test"));
    await context.Response.WriteAsync("Hello World " + context.Request.Protocol.ToString());
});

app.MapGet("/direct", async context =>
{
    context.Response.StatusCode = 200;
    var writer = context.Response.BodyWriter;
    var buffer = writer.GetSpan(11);
    "Hello World"u8.CopyTo(buffer);
    writer.Advance(11);
    await writer.FlushAsync();
});

app.MapGet("/direct50", async context =>
{
    context.Response.StatusCode = 200;
    var writer = context.Response.BodyWriter;
    var buffer = writer.GetSpan(50 * 1024);
    writer.Advance(50 * 1024);
    await writer.FlushAsync();
});

app.MapGet("/delay", async (HttpContext context) =>
{
    await Task.Delay(TimeSpan.FromMilliseconds(100));
});

app.MapPost("/post", context =>
{
    return context.Response.WriteAsync("Hello World " + context.Request.Protocol.ToString());
});

app.MapGet("/jsonresponse", () =>
{
    return TypedResults.Ok(new Data { Message = "Hello World" });
});

app.MapPost("/jsonrequest", (Data input) => Results.Ok(input.Message.Length));

app.MapGet("/echo", context => context.Request.Body.CopyToAsync(context.Response.Body));

app.Run();

public class Data
{
    public string Message { get; set; }
}

[JsonSerializable(typeof(Data))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}