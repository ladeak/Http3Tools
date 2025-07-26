using System.CommandLine;
using CHttp.Data;
using CHttp.Http;

namespace CHttp.Binders;

internal sealed class HttpRequestDetailsBinder
{
    private readonly Option<Uri> _uriOption;
    private readonly Option<HttpMethod> _httpMethodOption;
    private readonly Option<Version> _versionOption;
    private readonly Option<IEnumerable<KeyValueDescriptor>> _headerOption;

    public HttpRequestDetailsBinder(Option<HttpMethod> httpMethodOption, Option<Uri> uriOption, Option<Version> versionOption, Option<IEnumerable<KeyValueDescriptor>> headerOption)
    {
        _uriOption = uriOption;
        _httpMethodOption = httpMethodOption;
        _versionOption = versionOption;
        _headerOption = headerOption;
    }

    public HttpRequestDetails Bind(ParseResult parseResult, HttpContent? content = null)
    {
        var httpMethod = parseResult.GetRequiredValue(_httpMethodOption);
        var uri = parseResult.GetRequiredValue(_uriOption);
        var version = parseResult.GetRequiredValue(_versionOption);
        var headers = parseResult.GetRequiredValue(_headerOption);
        return new HttpRequestDetails(httpMethod, uri, version, headers) { Content = content };
    }
}
