using CHttp.Data;

public record HttpRequestDetails(HttpMethod Method, Uri Uri, Version Version, IEnumerable<KeyValueDescriptor> Headers)
{
    public HttpContent? Content { get; init; }
}