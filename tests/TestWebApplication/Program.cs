using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(kestrel =>
{
    kestrel.ListenAnyIP(5001, options =>
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

app.Run();
