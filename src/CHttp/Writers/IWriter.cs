using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

internal interface IWriter : IAsyncDisposable
{
    PipeWriter Buffer { get; }

    Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Encoding encoding);

    Task WriteSummaryAsync(Summary summary);

    Task CompleteAsync(CancellationToken token);
}