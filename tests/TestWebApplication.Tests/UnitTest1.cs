using CHttp.API;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TestWebApplication.Tests;

public class UnitTest1 : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _webapp;

    public UnitTest1(WebApplicationFactory<Program> webapp)
    {
        _webapp = webapp;
    }

    [Fact]
    public async Task Test()
    {
        var session = new PerformanceMeasureClient(new PerformanceMeasureClient.PerformanceOptions(10, 1));
        var stats = await session.RunAsync(_webapp.CreateClient(), () => new HttpRequestMessage(HttpMethod.Get, "/delay"));
    }
}