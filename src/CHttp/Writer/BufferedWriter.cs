using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using CHttp.Data;

internal abstract class BufferedWriter : IWriter
{
    protected Pipe _pipe;
    protected CancellationTokenSource _cts;
    protected Task _pipeReader;

    public PipeWriter Pipe => _pipe.Writer;

    public BufferedWriter()
    {
        _pipe = new Pipe();
        _cts = new CancellationTokenSource();
        _pipeReader = Task.CompletedTask;
    }

    protected Task RunAsync(PipeOptions? options = null)
    {
        _pipe = new Pipe(options ?? PipeOptions.Default);
        _cts = new CancellationTokenSource();
        _pipeReader = Task.Run(() => ReadPipeAsync(_cts.Token));
        return _pipeReader;
    }

    public abstract Task InitializeResponse(string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel);

    private async Task ReadPipeAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _pipe.Reader.ReadAsync(token);

                if (result.IsCanceled)
                    break;

                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    ProcessLine(line);
                }

                // Tell the PipeReader how much of the buffer has been consumed.
                _pipe.Reader.AdvanceTo(buffer.Start, buffer.End);

                // Stop reading if there's no more data coming.
                if (result.IsCompleted)
                    break;

            }
        }
        finally
        {
            await _pipe.Reader.CompleteAsync();
        }
    }

    protected abstract void ProcessLine(ReadOnlySequence<byte> line);

    internal bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? position = GetNextUtf8Position(buffer);

        if (!position.HasValue)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(0, position.Value));
        return true;
    }

    public abstract void WriteUpdate(Update update);

    public abstract void WriteSummary(Summary summary);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected SequencePosition? GetNextUtf8Position(in ReadOnlySequence<byte> source)
    {
        if (source.IsSingleSegment)
        {
            (int length, _) = GetUtf8Length(source.FirstSpan, 0);
            if (length == 0)
                return null;
            return source.GetPosition(length, source.Start);
        }
        else
        {
            return PositionOfMultiSegment(source);
        }
    }

    protected SequencePosition? PositionOfMultiSegment(in ReadOnlySequence<byte> source)
    {
        SequencePosition position = source.Start;
        SequencePosition result = position;
        int remainder = 0;
        while (source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            (var length, remainder) = GetUtf8Length(memory.Span, remainder);
            if (length != 0)
            {
                position = source.GetPosition(length, result);
            }
            else if (position.GetObject() == null)
            {
                break;
            }

            result = position;
        }

        return result;
    }

    protected (int BytesCount, int Remainder) GetUtf8Length(ReadOnlySpan<byte> source, int previousRemaining)
    {
        int length = 0;
        int currentCharBytes = previousRemaining;
        while (source.Length > 0)
        {
            if ((source[0] & 0b1000_0000) == 0)
                currentCharBytes = 1;
            else if ((source[0] & 0b1111_0000) == 0b1111_0000)
                currentCharBytes = 4;
            else if ((source[0] & 0b1110_0000) == 0b1110_0000)
                currentCharBytes = 3;
            else if ((source[0] & 0b1100_0000) == 0b1100_0000)
                currentCharBytes = 2;

            if (currentCharBytes > source.Length)
                return (length, currentCharBytes - source.Length);
            source = source.Slice(currentCharBytes);
            length += currentCharBytes;
        }
        return (length, 0);
    }

    public async Task CompleteAsync(CancellationToken token)
    {
        _cts.Cancel();
        await _pipeReader.WaitAsync(token);
    }

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
