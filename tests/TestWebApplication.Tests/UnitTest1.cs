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
        var client = _webapp.CreateClient();
        var response = await client.GetAsync("/delay");
        response.EnsureSuccessStatusCode();
    }
}