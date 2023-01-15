using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(kestrel =>
{
    kestrel.ListenAnyIP(5001, options =>
    {
        options.UseHttps();
        options.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
    });
});
builder.Services.AddHttpClient();
var app = builder.Build();

app.MapGet("/", async context =>
{
    await context.Response.WriteAsync("Hello World " + context.Request.Protocol.ToString());
});

app.MapGet("/long", async (HttpContext context, [FromServices] HttpClient client) =>
{
    var stream = await client.GetStreamAsync("https://www.gutenberg.org/files/1661/1661-0.txt");
    await stream.CopyToAsync(context.Response.Body);
});

app.MapGet("/longer", async (HttpContext context, [FromServices] HttpClient client) =>
{
    var stream = await client.GetStreamAsync("https://www.gutenberg.org/cache/epub/100/pg100.txt"); 
    await stream.CopyToAsync(context.Response.Body);
});

app.MapGet("/stream", GenerateData);

app.Run();

async IAsyncEnumerable<string> GenerateData()
{
    for (int i = 0; i < 10; i++)
    {
        await Task.Delay(1000);
        yield return $"hello {i}";
    }
}