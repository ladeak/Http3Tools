using System.IO.Pipelines;
using System.Net.Http.Headers;
using CHttp.Abstractions;
using CHttp.Writers;

namespace CHttpExecutor;

internal class VariablePostProcessingWriterStrategy : IWriter
{
    private Pipe _pipe;

    public VariablePostProcessingWriterStrategy(bool enabled)
    {
        Enabled = enabled;
        _pipe = new Pipe();
        Content = new MemoryStream();
        Buffer = _pipe.Writer;
    }

    public bool Enabled { get; private set; }

    public bool IsCompleted { get; private set; }

    public PipeWriter Buffer { get; }

    public HttpResponseHeaders? Headers { get; private set; }

    public HttpContentHeaders? ContentHeaders { get; private set; }

    public HttpResponseHeaders? Trailers { get; private set; }

    public MemoryStream Content { get; set; }

    public async Task CompleteAsync(CancellationToken token)
    {
        if (Enabled)
            await _pipe.Reader.CopyToAsync(Content, token);
        IsCompleted = true;
        Content.Seek(0, SeekOrigin.Begin);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary)
    {
        Trailers = trailers;
        return Task.CompletedTask;
    }

    public Task InitializeResponseAsync(HttpResponseInitials initials)
    {
        Headers = initials.Headers;
        ContentHeaders = initials.ContentHeaders;
        return Task.CompletedTask;
    }
}
