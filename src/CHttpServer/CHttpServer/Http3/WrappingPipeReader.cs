using System.Buffers;
using System.IO.Pipelines;
using CHttpServer.Http3;

internal sealed class WrappingPipeReader : PipeReader
{
    private readonly PipeReader _pipeReader;
    private StreamReadingStatus _streamReadingState = StreamReadingStatus.ReadingFrameHeader;
    private long _payloadRemainingLength = 0;
    private ReadOnlySequence<byte>? _lastPayloadReadBuffer;

    public WrappingPipeReader(PipeReader pipeReader)
    {
        _pipeReader = pipeReader;
    }

    public override void AdvanceTo(SequencePosition consumed)
    {
        AdvanceTo(consumed, consumed);
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (_lastPayloadReadBuffer.HasValue)
        {
            _payloadRemainingLength -= _lastPayloadReadBuffer.Value.Slice(0, consumed).Length;
            if (_payloadRemainingLength <= 0)
                _streamReadingState = StreamReadingStatus.ReadingFrameHeader;
        }
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

    private enum StreamReadingStatus
    {
        ReadingFrameHeader,
        ReadingPayloadData,
        ReadingPayloadReserved,
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken token = default)
    {
        while (true)
        {
            var readResult = await _pipeReader.ReadAsync(token);
            var buffer = readResult.Buffer;
            if (readResult.IsCanceled || (readResult.IsCompleted && readResult.Buffer.IsEmpty))
            {
                _lastPayloadReadBuffer = null;
                return readResult;
            }

            if (ProcessReadResult(readResult.Buffer, out var dataPayload))
                return new ReadResult(dataPayload, readResult.IsCanceled, readResult.IsCompleted);
        }
    }

    private bool ProcessReadResult(ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> dataPayload)
    {
        long bufferConsumed = 0;
        dataPayload = ReadOnlySequence<byte>.Empty;
        if (_streamReadingState == StreamReadingStatus.ReadingFrameHeader)
        {
            bufferConsumed = ReadFrameHeader(ref _payloadRemainingLength, ref _streamReadingState, buffer);
            _pipeReader.AdvanceTo(buffer.Slice(bufferConsumed).Start);
        }
        else if (_streamReadingState == StreamReadingStatus.ReadingPayloadData)
        {
            bufferConsumed = ReadFramePayload(_payloadRemainingLength, buffer);
            dataPayload = buffer.Slice(0, bufferConsumed);
            _lastPayloadReadBuffer = dataPayload;
            return true;
        }
        else if (_streamReadingState == StreamReadingStatus.ReadingPayloadReserved)
        {
            bufferConsumed = ReadFramePayload(_payloadRemainingLength, buffer);
            _pipeReader.AdvanceTo(buffer.Slice(bufferConsumed).Start);
            _payloadRemainingLength -= bufferConsumed;
            if (_payloadRemainingLength == 0)
                _streamReadingState = StreamReadingStatus.ReadingFrameHeader;
        }
        return false;
    }

    public override bool TryRead(out ReadResult readResult)
    {
        while (true)
        {
            var success = _pipeReader.TryRead(out readResult);
            var buffer = readResult.Buffer;
            if (readResult.IsCanceled || (readResult.IsCompleted && readResult.Buffer.IsEmpty))
            {
                _lastPayloadReadBuffer = null;
                return true;
            }

            if (ProcessReadResult(readResult.Buffer, out var dataPayload))
                readResult = new ReadResult(dataPayload, readResult.IsCanceled, readResult.IsCompleted);
            return !dataPayload.IsEmpty;
        }
    }

    private long ReadFrameHeader(ref long payloadRemainingLength, ref StreamReadingStatus streamReadingState, ReadOnlySequence<byte> buffer)
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

    private long ReadFramePayload(long payloadRemainingLength, ReadOnlySequence<byte> buffer)
    {
        var bufferConsumed = payloadRemainingLength < buffer.Length ? payloadRemainingLength : buffer.Length; // Read the complete reserved frame.
        return bufferConsumed;
    }

    private StreamReadingStatus NextRequestReadingState(ulong frameType)
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

    public override Task CopyToAsync(PipeWriter destination, CancellationToken cancellationToken = default)
    {
        // TODO
        throw new PlatformNotSupportedException();
    }

    public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        // TODO
        throw new PlatformNotSupportedException();
    }
}