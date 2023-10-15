using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

internal record struct HttpResponseInitials(HttpStatusCode ResponseStatus, HttpResponseHeaders Headers, HttpContentHeaders ContentHeaders, Version HttpVersion, Encoding Encoding);

internal interface IWriter : IAsyncDisposable
{
    PipeWriter Buffer { get; }

    Task InitializeResponseAsync(HttpResponseInitials initials);

    Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary);

    Task CompleteAsync(CancellationToken token);
}
