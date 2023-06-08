using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class SummaryWriter : IWriter
{
    private readonly SizeMeasuringPipeWriter _buffer;
    private readonly List<Summary> _summaries;

    public PipeWriter Buffer => _buffer;

    public IEnumerable<Summary> Summaries => _summaries;

    public SummaryWriter()
    {
        _buffer = new SizeMeasuringPipeWriter();
        _summaries = new List<Summary>();
    }

    public Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Version httpVersion, Encoding encoding)
    {
        _buffer.Reset();
        return Task.CompletedTask;
    }

    public Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary)
    {
        summary.Length = _buffer.Size;
        _summaries.Add(summary);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}