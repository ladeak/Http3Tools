using CHttp;
using CHttp.Abstractions;
using CHttp.Http;
using CHttp.Statitics;
using Microsoft.JavaScript.NodeApi;

namespace CHttpExtension;

[JSExport]
public static class CHttpExt
{
	public static string Run(HttpRequestDetails requestDetails, HttpBehavior httpBehavior, PerformanceBehavior performanceBehavior)
	{
		var console = new StringConsole();
		var cookieContainer = new MemoryCookieContainer();
		ISummaryPrinter printer = new StatisticsPrinter(console);
		var orchestrator = new PerformanceMeasureOrchestrator(printer, console, new Awaiter(), cookieContainer, performanceBehavior.Map());
		orchestrator.RunAsync(requestDetails.Map(), httpBehavior.Map())
			.GetAwaiter()
			.GetResult();
		return console.Text;
	}
}
