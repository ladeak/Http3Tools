using CHttp;
using CHttp.Abstractions;
using CHttp.Binders;
using CHttp.Data;
using CHttp.Http;
using CHttp.Statitics;
using Microsoft.JavaScript.NodeApi;

namespace CHttpExtension;

[JSExport]
public static class CHttpExt
{
	private static CancellationTokenSource _cancellationTokenSource = new();
	private static SemaphoreSlim _semaphore = new SemaphoreSlim(1);
	private static MemoryFileSystem _fileSystem = new MemoryFileSystem();

	public static async Task<string> RunAsync(
		string executionName,
		bool enableRedirects,
		bool enableCertificateValidation,
		double timeout,
		string method,
		string uri,
		string version,
		IEnumerable<string> headers,
		string content,
		int requestCount,
		int clientsCount,
		Action<string> callback
	)
	{
		_cancellationTokenSource.Cancel();
		if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
			return "Cancelled";
		try
		{
			_cancellationTokenSource = new();
			return await RunImplAsync(executionName, enableRedirects, enableCertificateValidation,
				timeout, method, uri, version, headers, content, requestCount, clientsCount, callback);
		}
		finally
		{
			_semaphore.Release();
		}
	}

	private static async Task<string> RunImplAsync(
		string? executionName,
		bool enableRedirects,
		bool enableCertificateValidation,
		double timeout,
		string method,
		string uri,
		string version,
		IEnumerable<string> headers,
		string content,
		int requestCount,
		int clientsCount,
		Action<string> callback
	)
	{
		var httpBehavior = new HttpBehavior(enableRedirects, enableCertificateValidation, timeout, false, string.Empty);
		var parsedHeaders = new List<KeyValueDescriptor>();
		foreach (string header in headers ?? Enumerable.Empty<string>())
		{
			parsedHeaders.Add(new KeyValueDescriptor(header));
		}
		var requestDetails = new HttpRequestDetails(
			new HttpMethod(method),
			new Uri(uri, UriKind.Absolute),
			VersionBinder.Map(version),
			parsedHeaders);
		var performanceBehavior = new PerformanceBehavior(requestCount, clientsCount);
		var console = new StringConsole();
		var cookieContainer = new MemoryCookieContainer();
		ISummaryPrinter printer;
		if (string.IsNullOrEmpty(executionName))
			printer = new StatisticsPrinter(console);
		else
			printer = new CompositePrinter(new StatisticsPrinter(console), new FilePrinter(executionName, _fileSystem));
		var orchestrator = new PerformanceMeasureOrchestrator(printer, new StateConsole(callback), new Awaiter(), cookieContainer, performanceBehavior);
		await orchestrator.RunAsync(requestDetails, httpBehavior, _cancellationTokenSource.Token);
		return console.Text;
	}

	public static async Task<string> GetDiffAsync(string defaultColor, string file1, string file2)
	{
		var console = new StringConsole(defaultColor);
		var session0 = await PerformanceFileHandler.LoadAsync(_fileSystem, file1);
		var session1 = await PerformanceFileHandler.LoadAsync(_fileSystem, file2);
		var comparer = new DiffPrinter(console);
		comparer.Compare(session0, session1);
		return console.Text;
	}

	public static void Cancel() => _cancellationTokenSource.Cancel();
}


