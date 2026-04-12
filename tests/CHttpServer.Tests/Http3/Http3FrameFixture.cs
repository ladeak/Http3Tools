using System.IO.Pipelines;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

internal class TestPipeWriter : PipeWriter
{
    private byte[] _buffer = new byte[4096];
    private int _consumedLength = 0;

    public ReadOnlySpan<byte> WrittenData => _buffer.AsSpan(0, _consumedLength);

    public override void Advance(int bytes)
    {
        _consumedLength += bytes;
    }

    public override void CancelPendingFlush()
    {
    }

    public override void Complete(Exception? exception = null)
    {
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new FlushResult());
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (_buffer == null || sizeHint < _buffer.Length - _consumedLength)
            Grow(sizeHint);
        return _buffer.AsMemory(_consumedLength);
    }

    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        if (_buffer == null || sizeHint < _buffer.Length - _consumedLength)
            Grow(sizeHint);
        return _buffer.AsSpan(_consumedLength);
    }

    private void Grow(int sizeHint)
    {
        var newSize = Math.Max(sizeHint + _consumedLength, _buffer.Length * 2);
        Array.Resize(ref _buffer, newSize);
    }
}

internal class Http3FrameFixture
{
    public static byte[] GetHeadersFrame()
    {
        var encoder = new QPackDecoder();
        var headers = new Http3ResponseHeaderCollection
        {
            { ":path", "/" },
            { ":authority", "localhost" },
            { ":method", "GET" },
            { ":scheme", "https" }
        };
        var writer = new TestPipeWriter();
        encoder.Encode(headers, writer);
        var payloadLength = VariableLenghtIntegerDecoder.Write(writer.WrittenData.Length);
        return [1, .. payloadLength.Span, .. writer.WrittenData];
    }

    public static byte[] GetLargeHeadersFrame()
    {
        var encoder = new QPackDecoder();
        var headers = new Http3ResponseHeaderCollection
        {
            { ":path", "/" },
            { ":authority", "localhost" },
            { ":method", "GET" },
            { ":scheme", "https" },
            { "x-custom-header", new string('a', 4096*2) }
        };
        var writer = new TestPipeWriter();
        encoder.Encode(headers, writer);
        var payloadLength = VariableLenghtIntegerDecoder.Write(writer.WrittenData.Length);
        return [1, .. payloadLength.Span, .. writer.WrittenData];
    }

    public static byte[] GetReservedFrame(int length, int seed = 2)
    {
        var frameType = VariableLenghtIntegerDecoder.Write(seed * 0x1f + 0x21);
        var payloadLength = VariableLenghtIntegerDecoder.Write(length);
        var payload = Enumerable.Sequence(0, length - 1, 1).Select(x => (byte)x);
        return [.. frameType.Span, .. payloadLength.Span, .. payload];
    }

    public static byte[] GetDataFrame(int length)
    {
        var payloadLength = VariableLenghtIntegerDecoder.Write(length);
        var payload = Enumerable.Sequence(0, length - 1, 1).Select(x => (byte)x);
        return [0, .. payloadLength.Span, .. payload];
    }
}
