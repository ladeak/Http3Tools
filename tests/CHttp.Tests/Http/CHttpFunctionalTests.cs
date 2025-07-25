using System.Text;
using CHttp.Abstractions;
using CHttp.Writers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Primitives;

namespace CHttp.Tests.Http;

public class CHttpFunctionalTests
{
    private const string DateReplacement = "2025.07.27";

    [Fact]
    public async Task VerboseWriter_TestVanilaHttp3Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), HttpProtocols.Http3);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite(filterDate: DateReplacement);
        var writer = new VerboseConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).Parse("--method GET --no-certificate-validation --uri https://localhost:5011").InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains($"Status: OK Version: 3.0 Encoding: utf-8{Environment.NewLine}Date:{DateReplacement}{Environment.NewLine}Server:Kestrel{Environment.NewLine}{Environment.NewLine}test{Environment.NewLine}https://localhost:5011/ 4 B 00:0", console.Text);
    }

    [Fact]
    public async Task VerboseWriter_TestVanilaHttp2Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite(filterDate: DateReplacement);
        var writer = new VerboseConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).Parse("--method GET --no-certificate-validation --uri https://localhost:5011 -v 2").InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains($"Status: OK Version: 2.0 Encoding: utf-8{Environment.NewLine}Date:{DateReplacement}{Environment.NewLine}Server:Kestrel{Environment.NewLine}{Environment.NewLine}test{Environment.NewLine}https://localhost:5011/ 4 B 00:0", console.Text);
    }

    [Fact]
    public async Task SilentWriter_TestVanilaHttp2Request()
    {
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            context.Response.Headers["my"] = new string('a', 1024 * 10);
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new SilentConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer)
            .Parse("--method GET --no-certificate-validation --uri https://localhost:5011 -v 2")
            .InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains($"Status: OK Version: 2.0 Encoding: utf-8{Environment.NewLine}https://localhost:5011/ 4 B 00:0", console.Text);
    }

    [Fact]
    public async Task CookiesAreSepartedBySemicolon()
    {
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            context.Response.Headers.Cookie = new StringValues(["a", "b"]);
            context.Response.Headers["my"] = new StringValues(["c", "d"]);
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsoleAsOuput();
        var writer = new VerboseConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).Parse("--method GET --no-certificate-validation --uri https://localhost:5011 -v 2").InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains("a;b", console.Text);
        Assert.Contains("c,d", console.Text);
    }

    [Fact]
    public async Task ContentTypeHeader_WrittenToConsole()
    {
        using var host = HttpServer.CreateHostBuilder(context =>
            context.Response.WriteAsJsonAsync("""{"message":"Hello World"}"""), HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new VerboseConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer)
            .Parse("--method GET --no-certificate-validation --uri https://localhost:5011 -v 2")
            .InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains($"Content-Type:application/json", console.Text);
    }

    [Fact]
    public async Task ProgressingWriter_TestVanilaHttp3Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), HttpProtocols.Http3);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new ProgressingConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).Parse("--method GET --no-certificate-validation --uri https://localhost:5011").InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains("100%       4 B", console.Text);
        Assert.Contains($"https://localhost:5011/ 4 B 00:0", console.Text);
    }

    [Theory]
    [InlineData("test message")]
    public async Task StreamWriter_TestVanilaHttp3Request(string response)
    {
        using var output = new MemoryStream();
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync(response), HttpProtocols.Http3);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new ProgressingConsoleWriter(new StreamBufferedProcessor(output), console);

        var client = await CommandFactory.CreateRootCommand(writer).Parse("--method GET --no-certificate-validation --uri https://localhost:5011").InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(response, Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData("headerValue")]
    public async Task TestingHeaders(string headerValue)
    {
        using var output = new MemoryStream();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Headers.TryGetValue("myheader", out var headers) && headers.Single() == headerValue)
                await context.Response.WriteAsync("test");
            else
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }, HttpProtocols.Http3);

        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new ProgressingConsoleWriter(new StreamBufferedProcessor(output), console);

        var client = await CommandFactory.CreateRootCommand(writer).Parse($"--method GET --no-certificate-validation --uri https://localhost:5011 --header=myheader:{headerValue}").InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("test", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public async Task CookieContainer_CookiesPersistedAcrossSessions()
    {
        bool cookieAttached = false;
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Cookies.TryGetValue("testKey", out var cookieValue))
                cookieAttached = true;
            context.Response.Cookies.Append("testKey", "someValue");
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new ProgressingConsoleWriter(new TextBufferedProcessor(), console);
        var MemoryFileSystem = new MemoryFileSystem();

        var client = await CommandFactory.CreateRootCommand(writer, fileSystem: MemoryFileSystem)
            .Parse("--method GET --no-certificate-validation --uri https://localhost:5011 --http-version 2 --cookie-container cookies.json")
            .InvokeAsync(TestContext.Current.CancellationToken);

        var client2 = await CommandFactory.CreateRootCommand(writer, fileSystem: MemoryFileSystem)
            .Parse("--method GET --no-certificate-validation --uri https://localhost:5011 --http-version 2 --cookie-container cookies.json").InvokeAsync(TestContext.Current.CancellationToken);

        Assert.True(cookieAttached);
        Assert.True(MemoryFileSystem.Exists("cookies.json"));
    }

    [Fact]
    public async Task NoCookieContainer_CookiesNotShared()
    {
        bool cookieAttached = false;
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Cookies.TryGetValue("testKey", out var cookieValue))
                cookieAttached = true;
            context.Response.Cookies.Append("testKey", "someValue");
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new ProgressingConsoleWriter(new TextBufferedProcessor(), console);
        var memoryFileSystem = new MemoryFileSystem();

        var client = await CommandFactory.CreateRootCommand(writer, fileSystem: memoryFileSystem)
            .Parse("--method GET --no-certificate-validation --uri https://localhost:5011 --http-version 2").InvokeAsync(TestContext.Current.CancellationToken);

        var client2 = await CommandFactory.CreateRootCommand(writer, fileSystem: memoryFileSystem)
            .Parse("--method GET --no-certificate-validation --uri https://localhost:5011 --http-version 2").InvokeAsync(TestContext.Current.CancellationToken);

        Assert.False(cookieAttached);
    }

    [Fact]
    public async Task ThrottledRequest_TestVanilaHttp2Request()
    {
        bool serverReadCompleted = false;
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            await context.Request.BodyReader.AsStream().CopyToAsync(Stream.Null);
            serverReadCompleted = true;
            await context.Response.WriteAsync("response");
        },
        protocol: HttpProtocols.Http2,
        configureKestrel: kestrelOptions =>
        {
            kestrelOptions.Limits.MinRequestBodyDataRate = new MinDataRate(1, TimeSpan.FromSeconds(20));
        });
        await host.StartAsync(TestContext.Current.CancellationToken);

        var console = new TestConsolePerWrite();
        var writer = new ProgressingConsoleWriter(new TextBufferedProcessor(), console);

        // 2 kbyte in UTF-8,should be 30 ms to complete with throttle speed of 1 kbyte/s
        string largeValue = new string('1', 2000);
        var client = CommandFactory.CreateRootCommand(writer)
            .Parse($$"""--method GET --no-certificate-validation --uri https://localhost:5011 --http-version 2 --body {""test"":""{{largeValue}}""} --upload-throttle 1""")
            .InvokeAsync(TestContext.Current.CancellationToken);

        // In 16 ms data is still being sent.
        await Task.Delay(TimeSpan.FromMilliseconds(16), TestContext.Current.CancellationToken);
        Assert.False(serverReadCompleted);
        await client;

        await writer.CompleteAsync(CancellationToken.None);
        Assert.True(serverReadCompleted);
        Assert.Contains("100%       8 B", console.Text);
    }

    [Fact]
    public async Task CustomContentType_TestVanilaHttp2Request()
    {
        bool valuesSet = false;
        using var host = HttpServer.CreateHostBuilder(configureApp: app =>
        {
            app.MapPost("/", ([FromForm] string name, [FromForm] string title) =>
            {
                if (name == "Alice" && title == "Software Engineer")
                    valuesSet = true;
                return "done";
            }).DisableAntiforgery();
        }, protocol: HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var console = new TestConsolePerWrite();
        var writer = new VerboseConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer)
            .Parse("--method POST --no-certificate-validation --uri https://localhost:5011/ -v 2 --header=\"Content-Type:application/x-www-form-urlencoded\" --body \"name=Alice&title=Software%20Engineer\"")
            .InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(valuesSet);
    }

    [Fact]
    public async Task JsonContentType_TestVanilaHttp2Request()
    {
        string fileName = "mycontent.json";
        var fileSystem = new MemoryFileSystem();
        CreateInputFile(fileSystem, fileName);
        using var host = HttpServer.CreateHostBuilder(configureApp: app =>
        {
            app.MapPost("/", ([FromBody] Request input) =>
            {
                if (input.Data != "Alice")
                    return Results.BadRequest();
                return Results.NoContent();
            }).DisableAntiforgery();
        }, protocol: HttpProtocols.Http2);
        await host.StartAsync(TestContext.Current.CancellationToken);

        var console = new TestConsolePerWrite();
        var writer = new VerboseConsoleWriter(new TextBufferedProcessor(), console);
        var client = await CommandFactory.CreateRootCommand(writer, fileSystem: fileSystem)
            .Parse($"""--method POST --no-certificate-validation --uri https://localhost:5011/ -v 2 --body {fileName} --header="Content-Type:application/json;charset=utf-8" """)
            .InvokeAsync(TestContext.Current.CancellationToken);

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains("Status: NoContent", console.Text);

        static void CreateInputFile(MemoryFileSystem fileSystem, string fileName)
        {
            var file = fileSystem.Open(fileName, FileMode.CreateNew, FileAccess.Write);
            ReadOnlySpan<byte> content = """{"Data":"Alice"}"""u8;
            file.Write(content);
            file.Close();
        }
    }

    private class Request
    {
        public string? Data { get; set; }
    }
}
