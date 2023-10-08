using Microsoft.JavaScript.NodeApi;

namespace CHttpExtension;

[JSExport]
public class HttpBehavior
{
	public bool EnableRedirects { get; set; }

	public bool EnableCertificateValidation { get; set; }

	public double Timeout { get; set; }

	internal CHttp.HttpBehavior Map()
	{
		return new CHttp.HttpBehavior(EnableRedirects, EnableCertificateValidation, Timeout, false, string.Empty);
	}
}