using System;
using System.Buffers;
using System.CommandLine.Parsing;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class NormalConsoleWriter : IWriter
{
    private readonly IBufferedProcessor _processor;
    private readonly IConsole _console;

    public PipeWriter Pipe => _processor.Pipe;

    public NormalConsoleWriter(IBufferedProcessor processor, IConsole console)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _console = console;
    }

    public async Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding)
    {
        _processor.Cancel();
        await _processor.CompleteAsync(CancellationToken.None);
        _ = _processor.RunAsync(ProcessLine);
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);
        _console.Write(buffer, 0, count);
        ArrayPool<char>.Shared.Return(buffer);
        return Task.CompletedTask;
    }

    public async Task WriteSummaryAsync(Summary summary)
    {
        await _processor.CompleteAsync(CancellationToken.None);
        _console.WriteLine(summary.ToString());
    }

    public Task CompleteAsync(CancellationToken token) => _processor.CompleteAsync(token);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
