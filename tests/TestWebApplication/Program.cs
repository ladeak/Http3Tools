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
	context.Response.Headers.TryAdd("x-custom", new Microsoft.Extensions.Primitives.StringValues("test"));
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

app.MapGet("/delay", async (HttpContext context) =>
{
	await Task.Delay(TimeSpan.FromMilliseconds(100));
});

app.MapGet("/stream", GenerateData);

app.MapPost("/post", context =>
{
	return context.Response.WriteAsync("Hello World " + context.Request.Protocol.ToString());
});

app.MapGet("/jsonresponse", context =>
{
	context.Response.Headers.ContentType = "application/json";
	return context.Response.WriteAsync("""{"message":"Hello World"}""");
});

app.MapPost("/jsonrequest", (Data input) => Results.Ok(input.Message?.Length ?? 0));

app.MapGet("/echo", context => context.Request.Body.CopyToAsync(context.Response.Body));

app.MapPost("/forms", ([FromForm] string name, [FromForm] string title) =>
{
	if (name == "Alice" && title == "Software Engineer")
		return Results.NoContent();
	return Results.BadRequest();
}).DisableAntiforgery();

app.Run();

async IAsyncEnumerable<string> GenerateData()
{
	for (int i = 0; i < 10; i++)
	{
		await Task.Delay(1000);
		yield return $"hello {i}";
	}
}

public class Data
{
	public string? Message { get; set; }
}
