using CHttp.Binders;
using CHttp.Data;
using Microsoft.JavaScript.NodeApi;

namespace CHttpExtension;

[JSExport]
public class HttpRequestDetails
{
	public string Method { get; set; }

	public string Uri { get; set; }

	public string Version { get; set; }

	public IEnumerable<string> Headers { get; set; }

	public string? Content { get; set; }

	internal CHttp.HttpRequestDetails Map()
	{
		var headers = new List<KeyValueDescriptor>();
		foreach (string header in Headers ?? Enumerable.Empty<string>())
		{
			headers.Add(new KeyValueDescriptor(header));
		}
		return new CHttp.HttpRequestDetails(
			new HttpMethod(Method),
			new Uri(Uri, UriKind.Absolute),
			VersionBinder.Map(Version),
			headers);
	}
}