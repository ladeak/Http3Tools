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

    public virtual Task InitializeResponse(long totalSize, string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel)
    {
        _pipe = new Pipe();
        _cts = new CancellationTokenSource();
        _pipeReader = Task.Run(() => ReadPipeAsync(_cts.Token));
        return Task.CompletedTask;
    }

    protected async Task CancelReader()
    {
        await _pipe.Reader.CompleteAsync();
        _cts.Cancel();
        await _pipeReader;
    }

    private async Task ReadPipeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var result = await _pipe.Reader.ReadAsync(token);

            if (result.IsCanceled)
                return;

            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
            {
                ProcessLine(line);
            }

            // Tell the PipeReader how much of the buffer has been consumed.
            _pipe.Reader.AdvanceTo(buffer.Start, buffer.End);

            // Stop reading if there's no more data coming.
            if (result.IsCompleted)
            {
                break;
            }

        }

        await _pipe.Reader.CompleteAsync();
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
}

internal sealed class StatefulBufferedConsoleWriter : BufferedWriter
{
    private long _totalSize;
    private long _processedSize;
    private (int Left, int Top) _initialPosition;
    private LogLevel _logLevel;

    public StatefulBufferedConsoleWriter()
    {
        _logLevel = LogLevel.Info;
    }

    public override async Task InitializeResponse(long totalSize, string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel)
    {
        await CancelReader();
        _initialPosition = Console.GetCursorPosition();
        _processedSize = 0;
        _totalSize = totalSize;
        _logLevel = logLevel;
        await base.InitializeResponse(totalSize, responseStatus, headers, encoding, logLevel);
    }

    protected override void ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);

        var currentPosition = Console.GetCursorPosition();
        Console.SetCursorPosition(_initialPosition.Left, _initialPosition.Top);
        int lineLength = Console.WindowWidth;

        // TODO write + update

        if (_logLevel != LogLevel.Quiet)
            Console.Write(buffer, 0, count);

        ArrayPool<char>.Shared.Return(buffer);
    }

    public override void WriteUpdate(Update update) => Console.WriteLine(update.ToString());

    public override void WriteSummary(Summary summary) => Console.WriteLine(summary.ToString());

}
