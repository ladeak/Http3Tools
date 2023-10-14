using CHttp;
using CHttp.Abstractions;
using CHttp.Binders;
using CHttp.Data;
using CHttp.Http;
using CHttp.Statitics;
using CHttp.Writers;
using Microsoft.JavaScript.NodeApi;

namespace CHttpExtension;

[JSExport]
public static class CHttpExt
{
	private static CancellationTokenSource _cancellationTokenSource = new();
	private static SemaphoreSlim _semaphore = new SemaphoreSlim(1);
	private static MemoryFileSystem _fileSystem = new MemoryFileSystem();
	private static string _fileNotExistsMessage = "Results for '{0}' are not available";

	public static async Task<string> SendRequestAsync(
		bool enableRedirects,
		bool enableCertificateValidation,
		double timeout,
		string method,
		string uri,
		string version,
		IEnumerable<string> headers,
		string body
)
	{
		_cancellationTokenSource.Cancel();
		if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
			return "Cancelled";
		try
		{
			_cancellationTokenSource = new();
			return await SendRequestImplAsync(enableRedirects, enableCertificateValidation,
				timeout, method, uri, version, headers, body);
		}
		finally
		{
			_semaphore.Release();
		}
	}

	private static async Task<string> SendRequestImplAsync(
		bool enableRedirects,
		bool enableCertificateValidation,
		double timeout,
		string method,
		string uri,
		string version,
		IEnumerable<string> headers,
		string body
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
		if (!string.IsNullOrWhiteSpace(body))
			requestDetails = requestDetails with { Content = new StringContent(body) };
		var outputBehavior = new OutputBehavior(LogLevel.Verbose, string.Empty);
		var console = new StringConsole();
		var cookieContainer = new MemoryCookieContainer();
		var writer = new WriterStrategy(outputBehavior);
		var client = new HttpMessageSender(writer, cookieContainer, httpBehavior);
		await client.SendRequestAsync(requestDetails);
		await writer.CompleteAsync(CancellationToken.None);
		await cookieContainer.SaveAsync();

		return console.Text;
	}


	public static async Task<string> PerfMeasureAsync(
		string executionName,
		bool enableRedirects,
		bool enableCertificateValidation,
		double timeout,
		string method,
		string uri,
		string version,
		IEnumerable<string> headers,
		string body,
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
			return await PerfMeasureImplAsync(executionName, enableRedirects, enableCertificateValidation,
				timeout, method, uri, version, headers, body, requestCount, clientsCount, callback);
		}
		finally
		{
			_semaphore.Release();
		}
	}

	private static async Task<string> PerfMeasureImplAsync(
		string? executionName,
		bool enableRedirects,
		bool enableCertificateValidation,
		double timeout,
		string method,
		string uri,
		string version,
		IEnumerable<string> headers,
		string body,
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
		if (!string.IsNullOrWhiteSpace(body))
			requestDetails = requestDetails with { Content = new StringContent(body) };

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

	public static async Task<string> GetDiffAsync(string file1, string file2)
	{
		if (!FilesExist(file1, file2, out var validationMessage))
			throw new ArgumentException(validationMessage);
		var console = new StringConsole();
		var session0 = await PerformanceFileHandler.LoadAsync(_fileSystem, file1);
		var session1 = await PerformanceFileHandler.LoadAsync(_fileSystem, file2);
		var comparer = new DiffPrinter(console);
		comparer.Compare(session0, session1);
		return console.Text;
	}

	private static bool FilesExist(string fileName1, string fileName2, out string error)
	{
		if (!_fileSystem.Exists(fileName1))
		{
			error = string.Format(null, _fileNotExistsMessage, fileName1);
			return false;
		}
		if (!_fileSystem.Exists(fileName2))
		{
			error = string.Format(null, _fileNotExistsMessage, fileName2);
			return false;
		}
		error = string.Empty;
		return true;
	}

	public static void Cancel() => _cancellationTokenSource.Cancel();
}


