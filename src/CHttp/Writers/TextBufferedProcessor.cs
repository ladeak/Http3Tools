using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace CHttp.Writers;

internal class TextBufferedProcessor : IBufferedProcessor
{
    private Pipe _pipe;
    private CancellationTokenSource _cts;
    private Task _pipeReader;
    private long _position;

    public TextBufferedProcessor()
    {
        _pipe = new Pipe();
        _cts = new CancellationTokenSource();
        _pipeReader = Task.CompletedTask;
    }

    public PipeWriter Pipe => _pipe.Writer;

    public long Position => _position;

    public Task RunAsync(Func<ReadOnlySequence<byte>, Task> lineProcessor, PipeOptions? options = null)
    {
        _pipe = new Pipe(options ?? PipeOptions.Default);
        _cts = new CancellationTokenSource();
        _pipeReader = Task.Run(() => ReadPipeAsync(lineProcessor, _cts.Token));
        return _pipeReader;
    }

    private async Task ReadPipeAsync(Func<ReadOnlySequence<byte>, Task> lineProcessor, CancellationToken token)
    {
        try
        {
            _position = 0;
            while (!token.IsCancellationRequested)
            {
                var result = await _pipe.Reader.ReadAsync(token);

                if (result.IsCanceled)
                    break;

                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    unchecked
                    {
                        _position += line.Length;
                    }
                    await lineProcessor(line);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SequencePosition? GetNextUtf8Position(in ReadOnlySequence<byte> source)
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

    private SequencePosition? PositionOfMultiSegment(in ReadOnlySequence<byte> source)
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

    private (int BytesCount, int Remainder) GetUtf8Length(ReadOnlySpan<byte> source, int previousRemaining)
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
        await _pipeReader.WaitAsync(token);
    }

    public void Cancel() => _cts.Cancel();

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
