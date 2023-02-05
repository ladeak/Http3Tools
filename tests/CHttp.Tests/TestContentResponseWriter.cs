using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Tests;

internal class TestContentResponseWriter : IWriter
{
    public Task ReadCompleted => _pipeReader;

    public PipeWriter Pipe => _bufferedProcessor.Pipe;

    private readonly StringBuilder _sb = new StringBuilder();
    private readonly IBufferedProcessor _bufferedProcessor;
    private Task? _pipeReader;

    public TestContentResponseWriter(IBufferedProcessor bufferedProcessor)
    {
        _bufferedProcessor = bufferedProcessor ?? throw new ArgumentNullException(nameof(bufferedProcessor));
    }

    public Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding)
    {
        _pipeReader = _bufferedProcessor.RunAsync(ProcessLine);
        return Task.CompletedTask;
    }

    public Task ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);
        _sb.Append(buffer.AsSpan(0, count));
        ArrayPool<char>.Shared.Return(buffer);
        return Task.CompletedTask;
    }

    public override string ToString() => _sb.ToString();

    public void WriteSummary(Summary summary) => _sb.Append(summary.ToString());

    public Task CompleteAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
