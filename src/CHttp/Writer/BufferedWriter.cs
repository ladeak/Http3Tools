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

    protected Task RunAsync()
    {
        _pipe = new Pipe();
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

    protected bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
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
            int length = GetUtf8Length(source.FirstSpan);
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
        while (source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            var length = GetUtf8Length(memory.Span);
            if (length != 0)
            {
                return source.GetPosition(length, result);
            }
            else if (position.GetObject() == null)
            {
                break;
            }

            result = position;
        }

        return null;
    }

    protected int GetUtf8Length(ReadOnlySpan<byte> source)
    {
        int length = 0;
        while (source.Length > 0)
        {
            if ((source[0] & 0b1000_0000) == 0)
            {
                source = source.Slice(1);
                length += 1;
            }
            else if ((source[0] & 0b1111_0000) == 0b1111_0000)
            {
                source = source.Slice(4);
                length += 4;
            }
            else if ((source[0] & 0b1110_0000) == 0b1110_0000)
            {
                source = source.Slice(3);
                length += 3;
            }
            else if ((source[0] & 0b1100_0000) == 0b1100_0000)
            {
                source = source.Slice(2);
                length += 2;
            }
        }
        return length;
    }

    public async Task CompleteAsync(CancellationToken token)
    {
        _cts.Cancel();
        await _pipeReader.WaitAsync(token);
    }

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
