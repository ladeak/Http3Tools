using System.Buffers;
using System.IO.Pipelines;
using CHttpServer.Http3;

// TODO buffer segments

internal sealed class Http3DeframingPipeReader : PipeReader
{
    private enum StreamReadingStatus
    {
        ReadingFrameHeader,
        ReadingPayloadData,
        ReadingPayloadReserved,
    }

    private class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> data, object? source, int sourceOffset, long framePayloadRemaining)
        {
            base.Memory = data;
            Source = source;
            SourceOffset = sourceOffset;
            FramePayloadRemaining = framePayloadRemaining;
        }

        public object? Source { get; }
        public int SourceOffset { get; }
        public long FramePayloadRemaining { get; }

        public Segment SetNext(Segment s)
        {
            base.Next = s;
            s.UpdateRunningIndex(this.RunningIndex + this.Memory.Length);
            return s;
        }

        public void UpdateRunningIndex(long sum)
        {
            this.RunningIndex = sum;
            if (Next is Segment s)
                s.UpdateRunningIndex(sum + Memory.Length);
        }

        public Segment Slice(int start)
        {
            if (start < Memory.Length)
            {
                Memory = Memory.Slice(start);
                UpdateRunningIndex(0);
                return this;
            }
            if (Next is Segment s)
                return s.Slice(start - Memory.Length);
            throw new InvalidOperationException("Not a Segment");
        }

        public SequencePosition End => new SequencePosition(this, Memory.Length);
    }

    private PipeReader _pipeReader;
    private StreamReadingStatus _streamReadingState = StreamReadingStatus.ReadingFrameHeader;
    private long _payloadRemainingLength = 0;
    private Segment? _head;
    private Segment? _tail;

    public Http3DeframingPipeReader(PipeReader pipeReader)
    {
        _pipeReader = pipeReader;
    }

    public void Reset(PipeReader pipeReader)
    {
        // This class is not resetting the uderlying pipe reader.
        _pipeReader = pipeReader;
        _head = _tail = null;
        _payloadRemainingLength = 0;
        _streamReadingState = StreamReadingStatus.ReadingFrameHeader;
    }

    private void AddSegment(Segment s)
    {
        if (_tail == null)
            _head = _tail = s;
        else
            _tail = _tail.SetNext(s);
    }

    public override void AdvanceTo(SequencePosition consumed)
    {
        AdvanceTo(consumed, consumed);
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (consumed.GetObject() is not Segment segmentConsumed
            || consumed.GetObject() is not Segment segmentExamined)
        {
            if (_head != null && _tail != null)
                _pipeReader.AdvanceTo(consumed, examined);
            return;
        }

        var segmentOffset = consumed.GetInteger();

        var sourceOffset = segmentConsumed.SourceOffset + segmentOffset;
        var sourceObject = segmentConsumed.Source;
        consumed = new SequencePosition(sourceObject, sourceOffset);

        sourceOffset = segmentExamined.SourceOffset + examined.GetInteger();
        sourceObject = segmentExamined.Source;
        examined = new SequencePosition(sourceObject, sourceOffset);

        _payloadRemainingLength = segmentConsumed.FramePayloadRemaining - segmentOffset;
        if (_payloadRemainingLength == 0)
            _streamReadingState = StreamReadingStatus.ReadingFrameHeader;
        else
            _streamReadingState = StreamReadingStatus.ReadingPayloadData;

        _pipeReader.AdvanceTo(consumed, examined);
    }

    public override void CancelPendingRead()
    {
        _pipeReader.CancelPendingRead();
    }

    public override void Complete(Exception? exception = null)
    {
        _pipeReader.Complete(exception);
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken token = default)
    {
        while (true)
        {
            var readResult = await _pipeReader.ReadAsync(token);
            var buffer = readResult.Buffer;
            if (readResult.IsCanceled || (readResult.IsCompleted && readResult.Buffer.IsEmpty))
                return readResult;

            if (ProcessReadResult(readResult.Buffer, out var sequence))
                return new ReadResult(sequence, readResult.IsCanceled, readResult.IsCompleted);
            else
                _pipeReader.AdvanceTo(buffer.End);
        }
    }

    // A buffer may contain a complete DATA frame. It may also only contain the beginning
    // of the data frame. It may contain multiple DATA frames, where the last DATA frame 
    // is not fully contained in the sequence.
    // Advance later might not read all data, but only up to a certain point. It needs to
    // restore the 'reading state' (payload length and StreamReadingStatus) up to that point.
    // The StreamReadingStatus is always ReadingPayloadData, because there are no segments
    // created for the other frames. The 'remaining payload length' will capture the point
    // at the beginning of the segment.
    private bool ProcessReadResult(ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> sequence)
    {
        _head = _tail = null;
        sequence = ReadOnlySequence<byte>.Empty;
        long bufferConsumed = 0;
        var currentPayloadRemainingLength = _payloadRemainingLength;
        while (!buffer.IsEmpty)
        {
            if (_streamReadingState == StreamReadingStatus.ReadingFrameHeader)
            {
                bufferConsumed = ReadFrameHeader(ref currentPayloadRemainingLength, ref _streamReadingState, buffer);
            }
            else if (_streamReadingState == StreamReadingStatus.ReadingPayloadData)
            {
                bufferConsumed = ReadFramePayload(currentPayloadRemainingLength, buffer);
                var dataPayload = buffer.Slice(0, bufferConsumed);
                int currentPosition = 0;
                foreach (var s in dataPayload)
                {
                    var position = buffer.GetPosition(currentPosition);
                    var segment = new Segment(s, position.GetObject(), position.GetInteger(), currentPayloadRemainingLength);
                    currentPayloadRemainingLength -= s.Length;
                    AddSegment(segment);
                    currentPosition += s.Length;
                }
            }
            else if (_streamReadingState == StreamReadingStatus.ReadingPayloadReserved)
            {
                bufferConsumed = ReadFramePayload(currentPayloadRemainingLength, buffer);
                currentPayloadRemainingLength -= bufferConsumed;
            }

            buffer = buffer.Slice(bufferConsumed);
            if (currentPayloadRemainingLength == 0)
                _streamReadingState = StreamReadingStatus.ReadingFrameHeader;
        }

        _payloadRemainingLength = currentPayloadRemainingLength;

        // No data segments at all.
        if (_head == null || _tail == null)
            return false;

        sequence = new ReadOnlySequence<byte>(_head, 0, _tail, _tail.Memory.Length);
        return true;
    }

    public override bool TryRead(out ReadResult readResult)
    {
        while (true)
        {
            var success = _pipeReader.TryRead(out readResult);
            var buffer = readResult.Buffer;
            if (readResult.IsCanceled || (readResult.IsCompleted && readResult.Buffer.IsEmpty))
                return true;

            if (ProcessReadResult(readResult.Buffer, out var sequence))
            {
                readResult = new ReadResult(sequence, readResult.IsCanceled, readResult.IsCompleted);
                return !sequence.IsEmpty;
            }
            else
                _pipeReader.AdvanceTo(buffer.End);
        }
    }

    private static long ReadFrameHeader(ref long payloadRemainingLength, ref StreamReadingStatus streamReadingState, ReadOnlySequence<byte> buffer)
    {
        if (!VariableLenghtIntegerDecoder.TryRead(buffer, out var frameType, out int bytesReadFrameType))
            return 0; // Not enough data to read payload length.

        buffer = buffer.Slice(bytesReadFrameType);
        if (!VariableLenghtIntegerDecoder.TryRead(buffer, out var payloadLength, out int bytesReadPayloadLength))
            return 0; // Not enough data to read payload length.

        payloadRemainingLength = checked((long)payloadLength);
        streamReadingState = NextRequestReadingState(frameType);
        var bufferConsumed = bytesReadFrameType + bytesReadPayloadLength;
        return bufferConsumed;
    }

    private static long ReadFramePayload(long payloadRemainingLength, ReadOnlySequence<byte> buffer)
    {
        var bufferConsumed = payloadRemainingLength < buffer.Length ? payloadRemainingLength : buffer.Length; // Read the complete reserved frame.
        return bufferConsumed;
    }

    private static StreamReadingStatus NextRequestReadingState(ulong frameType)
    {
        StreamReadingStatus streamReadingState;
        switch (frameType)
        {
            case 0x0: // DATA
                streamReadingState = StreamReadingStatus.ReadingPayloadData;
                break;
            case 0x1: // HEADERS
                throw new Http3ConnectionException(ErrorCodes.H3FrameUnexpected);
            default:
                if ((frameType - 0x21) % 0x1f != 0)
                    throw new Http3ConnectionException(ErrorCodes.H3FrameUnexpected);
                streamReadingState = StreamReadingStatus.ReadingPayloadReserved;
                break;
        }
        return streamReadingState;
    }

    public override async Task CopyToAsync(PipeWriter destination, CancellationToken cancellationToken = default)
    {
        StreamReadingStatus streamState = StreamReadingStatus.ReadingFrameHeader;
        long payloadRemainingLength = 0;
        while (true)
        {
            var readResult = await _pipeReader.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            if (readResult.IsCanceled)
            {
                await destination.CompleteAsync(new OperationCanceledException());
                return;
            }
            if (readResult.IsCompleted && readResult.Buffer.IsEmpty)
            {
                await destination.CompleteAsync();
                return;
            }
            (payloadRemainingLength, streamState) = await CopyReadResultAsync(buffer, destination, CopyToPipe, streamState, payloadRemainingLength, cancellationToken);
            _pipeReader.AdvanceTo(readResult.Buffer.End);
            if (readResult.IsCompleted && payloadRemainingLength == 0)
            {
                await destination.CompleteAsync();
                return;
            }
        }

        static ValueTask CopyToPipe(ReadOnlyMemory<byte> data, PipeWriter dest, CancellationToken token)
        {
            var copy = dest.WriteAsync(data, token);
            if (copy.IsCompletedSuccessfully)
            {
                copy.GetAwaiter().GetResult();
                return ValueTask.CompletedTask;
            }
            return AwaitCopy(copy);

            static async ValueTask AwaitCopy(ValueTask<FlushResult> copyTask)
            {
                await copyTask;
            }
        }
    }

    public override async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        StreamReadingStatus streamState = StreamReadingStatus.ReadingFrameHeader;
        long payloadRemainingLength = 0;
        while (true)
        {
            var readResult = await _pipeReader.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            if (readResult.IsCanceled || (readResult.IsCompleted && readResult.Buffer.IsEmpty))
                return;
            (payloadRemainingLength, streamState) = await CopyReadResultAsync(buffer, destination, static (data, dest, token) => dest.WriteAsync(data, token), streamState, payloadRemainingLength, cancellationToken);
            _pipeReader.AdvanceTo(readResult.Buffer.End);
            if (readResult.IsCompleted && payloadRemainingLength == 0)
                return;
        }
    }

    private static async ValueTask<(long, StreamReadingStatus)> CopyReadResultAsync<TDestination>(
        ReadOnlySequence<byte> buffer,
        TDestination destination,
        Func<ReadOnlyMemory<byte>, TDestination, CancellationToken, ValueTask> write,
        StreamReadingStatus state,
        long payloadRemainingLength,
        CancellationToken token)
    {
        while (!buffer.IsEmpty)
        {
            long bufferConsumed = 0;
            if (state == StreamReadingStatus.ReadingFrameHeader)
            {
                bufferConsumed = ReadFrameHeader(ref payloadRemainingLength, ref state, buffer);
            }
            else if (state == StreamReadingStatus.ReadingPayloadData)
            {
                bufferConsumed = ReadFramePayload(payloadRemainingLength, buffer);
                payloadRemainingLength -= bufferConsumed;
                var dataPayload = buffer.Slice(0, bufferConsumed);
                foreach (var s in dataPayload)
                    await write(s, destination, token);
            }
            else if (state == StreamReadingStatus.ReadingPayloadReserved)
            {
                bufferConsumed = ReadFramePayload(payloadRemainingLength, buffer);
                payloadRemainingLength -= bufferConsumed;
            }

            buffer = buffer.Slice(bufferConsumed);
            if (payloadRemainingLength == 0)
                state = StreamReadingStatus.ReadingFrameHeader;
        }

        return (payloadRemainingLength, state);
    }
}