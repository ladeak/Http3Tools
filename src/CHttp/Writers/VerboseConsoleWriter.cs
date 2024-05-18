using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Abstractions;

namespace CHttp.Writers;

internal sealed class VerboseConsoleWriter : IWriter
{
    private readonly IBufferedProcessor _contentProcessor;
    private readonly IConsole _console;

    public PipeWriter Buffer => _contentProcessor.Pipe;

    public VerboseConsoleWriter(IBufferedProcessor contentProcessor, IConsole console)
    {
        _contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
        _console = console;
    }

    public async Task InitializeResponseAsync(HttpResponseInitials initials)
    {
        _contentProcessor.Cancel();
        await _contentProcessor.CompleteAsync(CancellationToken.None);

        PrintResponse(initials);
        _ = _contentProcessor.RunAsync(ProcessLine);
    }

    private void PrintResponse(HttpResponseInitials initials)
    {
        _console.WriteLine($"Status: {initials.ResponseStatus} Version: {initials.HttpVersion} Encoding: {initials.Encoding.WebName}");
        HeaderWriter.Write(initials.Headers, _console);
        HeaderWriter.Write(initials.ContentHeaders, _console);
        _console.WriteLine();
	}

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);
        _console.Write(buffer[..count]);
        ArrayPool<char>.Shared.Return(buffer);
        return Task.CompletedTask;
    }

    public async Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary)
    {
        await _contentProcessor.CompleteAsync(CancellationToken.None);
        _console.WriteLine();
        if (trailers is { })
            HeaderWriter.Write(trailers, _console);
        summary.Length = _contentProcessor.Position;
        _console.WriteLine(summary.ToString());
    }

    public Task CompleteAsync(CancellationToken token) => _contentProcessor.CompleteAsync(token);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
