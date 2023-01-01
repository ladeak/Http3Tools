public record HttpRequestDetails(HttpMethod Method, Uri Uri, Version Version, IEnumerable<KeyValueDescriptor> Headers, double Timeout)
{
    public HttpContent? Content { get; init; }
}