using System.Buffers;
using System.CommandLine;
using System.Text.Json;
using CHttp.Abstractions;
using CHttp.Statitics;
using Microsoft.AspNetCore.Http;

namespace CHttp.Tests;

public class CHttpPerformanceFunctionalTests
{
	private const int Port = 5015;

	[Theory]
	[InlineData("test message")]
	public async Task TestPerformance_OutputsBasicResults(string response)
	{
		using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync(response), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3, port: Port);
		await host.StartAsync();
		var console = new TestConsolePerWrite();

		var client = await CommandFactory.CreateRootCommand(console: console).InvokeAsync($"perf --method GET --no-certificate-validation --uri https://localhost:{Port} -c 2 -n 2 -v 3")
			.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Contains("[=-----]      0/0", console.Text);
		Assert.Contains("100%          2/2", console.Text);
		Assert.Contains("1xx: 0, 2xx: 2, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0", console.Text);
		Assert.Contains("Mean:", console.Text);
		Assert.Contains("95th:", console.Text);
		Assert.Contains("Req/Sec:", console.Text);

		/* Expect something like this in the output, but actual details depend on the test run.
        [=-----]      0/0
        [-=----]      0/0
        [--=---]      0/0
        [---=--]      0/0
        [----=-]      0/0
        [-----=]      0/0
        [=-----]      0/0
        [-=----]      0/0
        [--=---]      0/0
        [---=--]      2/2
        100%          2/2

        | Mean:           22,878 ms   |
        | StdDev:        100,000 ns   |
        | Error:          70,711 ns   |
        | Median:         22,879 ms   |
        | Min:            22,878 ms   |
        | Max:            22,879 ms   |
        | Throughput:      1,054 MB/s |
        | Req/Sec:          43,7      |
        -----------------------------------------------------------
        HTTP status codes:
        1xx: 0, 2xx: 2, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0
        -----------------------------------------------------------
        */
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(3, 9)]
	[InlineData(10, 20)]
	public async Task TestNumberOfClients_Requests(int clients, int requests)
	{
		HashSet<string> connectionIds = new();
		int requestCounter = 0;
		using var host = HttpServer.CreateHostBuilder(context =>
		{
			lock (connectionIds)
			{
				connectionIds.Add(context.Connection.Id);
				requestCounter++;
			}
			return context.Response.WriteAsync("response");
		}, Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2, port: Port);
		await host.StartAsync();
		var console = new TestConsolePerWrite();

		var client = await CommandFactory.CreateRootCommand(console: console).InvokeAsync($"perf --method GET --no-certificate-validation --uri https://localhost:{Port} -c {clients} -n {requests} -v 2")
			.WaitAsync(TimeSpan.FromSeconds(10));

		// Each client does a preflight warnup request.
		Assert.Equal(requests + clients, requestCounter);
		Assert.Equal(clients, connectionIds.Count);
	}


	[Theory]
	[InlineData("test message")]
	public async Task TestPerformance_WritesToOutputFile(string response)
	{
		using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync(response), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2, port: Port);
		await host.StartAsync();
		var fileSystem = new MemoryFileSystem();
		var console = new TestConsolePerWrite();
		const int count = 2;
		var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem)
			.InvokeAsync($"perf --method GET --no-certificate-validation --uri https://localhost:{Port} -c 2 -n {count} -v 2 -o file.json")
			.WaitAsync(TimeSpan.FromSeconds(10));

		var data = fileSystem.GetFile("file.json");
		var results = JsonSerializer.Deserialize<PerformanceMeasurementResults>(data);
		Assert.Equal(count, results!.Summaries.Count);
		Assert.True(results.TotalBytesRead > 0);
	}

	[Fact]
	public async Task WithContent_WritesToOutputFile()
	{
		string content = nameof(content);
		bool received = true;
		using var host = HttpServer.CreateHostBuilder(async context =>
		{
			var buffer = ArrayPool<byte>.Shared.Rent(7);
			int count = await context.Request.Body.ReadAsync(buffer.AsMemory());
			received &= buffer.AsSpan(0, count).SequenceEqual("content"u8);
			ArrayPool<byte>.Shared.Return(buffer);
			await context.Response.WriteAsync("response");
		}, Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2, port: Port);
		await host.StartAsync();
		var console = new TestConsolePerWrite();
		var client = await CommandFactory.CreateRootCommand(console: console)
			.InvokeAsync($"perf --method GET --no-certificate-validation --uri https://localhost:{Port} -c 2 -n 4 -v 2 -b {content}")
			.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Contains("2xx: 4", console.Text);
		Assert.True(received);
	}
}
