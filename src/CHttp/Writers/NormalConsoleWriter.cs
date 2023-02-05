using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class NormalConsoleWriter : IWriter
{
    private Task _progress;
    private CancellationTokenSource _cts;
    private long _responseSize;
    private readonly IBufferedProcessor _processor;

    public PipeWriter Pipe => throw new NotImplementedException();

    public NormalConsoleWriter(IBufferedProcessor processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _progress = Task.CompletedTask;
        _cts = new CancellationTokenSource();
    }

    public async Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding)
    {
        await CompleteAsync(CancellationToken.None);
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _responseSize = 0;
        _ = _processor.RunAsync(ProcessLine);
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);
        Console.Write(buffer, 0, count);
        ArrayPool<char>.Shared.Return(buffer);
        return Task.CompletedTask;
    }

    public void WriteSummary(Summary summary)
    {
        _cts.Cancel();
        Console.WriteLine(summary.ToString());
    }

    public Task CompleteAsync(CancellationToken token) => _processor.CompleteAsync(token);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
