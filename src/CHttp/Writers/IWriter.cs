using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

internal interface IWriter : IAsyncDisposable
{
    PipeWriter Buffer { get; }

    Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Version httpVersion, Encoding encoding);

    Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary);

    Task CompleteAsync(CancellationToken token);
}