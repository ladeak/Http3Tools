namespace CHttpExtension;

public class PerformanceBehavior
{
	public int RequestCount { get; set; }

	public int ClientsCount { get; set; }

	internal CHttp.PerformanceBehavior Map()
	{
		return new CHttp.PerformanceBehavior(RequestCount, ClientsCount);
	}
}
