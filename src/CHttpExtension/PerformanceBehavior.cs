using Microsoft.JavaScript.NodeApi;

namespace CHttpExtension;

[JSExport]
public class PerformanceBehavior
{
	public int RequestCount { get; set; }

	public int ClientsCount { get; set; }

	internal CHttp.PerformanceBehavior Map()
	{
		return new CHttp.PerformanceBehavior(RequestCount, ClientsCount);
	}
}
