using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;

internal interface IWriter : IAsyncDisposable
{
    PipeWriter Pipe { get; }

    Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding);

    void WriteSummary(Summary summary);

    Task CompleteAsync(CancellationToken token);
}