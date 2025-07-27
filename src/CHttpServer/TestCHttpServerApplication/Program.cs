using CHttpServer;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.UseCHttpServer();
var app = builder.Build();
app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/a", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    //return forecast;
    return TypedResults.Ok(forecast);
});

app.MapPost("/a", ([FromBody] SampleRequest a, CancellationToken token) =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return TypedResults.Ok(forecast);
});
app.MapGet("/stream", async (HttpContext ctx) =>
                                  {
                                      ctx.Response.StatusCode = 200;
                                      await ctx.Response.WriteAsync("some content");
                                      await Task.Delay(5000);
                                      await ctx.Response.WriteAsync("some content2");
                                  });

app.MapGet("/stream2", GetStream);

async IAsyncEnumerable<string> GetStream()
{
    foreach (var i in Enumerable.Range(0, 10))
    {
        await Task.Delay(1000);
        yield return "some content";
    }
}

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

internal class SampleRequest
{
    public required string A { get; set; }
}