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

	public static async Task<string> Run(
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
		if (!_cancellationTokenSource.TryReset())
			_cancellationTokenSource = new();
		_cancellationTokenSource = new();
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
		ISummaryPrinter printer = new StatisticsPrinter(console);
		var orchestrator = new PerformanceMeasureOrchestrator(printer, new StateConsole(callback), new Awaiter(), cookieContainer, performanceBehavior);
		await orchestrator.RunAsync(requestDetails, httpBehavior, _cancellationTokenSource.Token);
		return console.Text;
	}

	public static void Cancel() => _cancellationTokenSource.Cancel();
}
