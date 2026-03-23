using System.IO.Pipelines;
using CHttpServer.Http3;
using static CHttpServer.Http3.QPackDecoder;

namespace CHttpServer.Tests.Http3;

public class QPackEncoderTests
{
    [Fact]
    public async Task EncodeStatusCode200()
    {
        var stream = new MemoryStream();
        var writer = new Http3FramingStreamWriter(stream, 1);
        var sut = new QPackDecoder();
        sut.Encode(200, new Http3ResponseHeaderCollection(), writer);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        byte[] expected = [1, 3, 0, 0, 25 | 0b1100_0000];
        Assert.True(expected.SequenceEqual(stream.ToArray()));
    }

    [Fact]
    public async Task EncodeStatusCode103()
    {
        var stream = new MemoryStream();
        var writer = new Http3FramingStreamWriter(stream, 2);
        var sut = new QPackDecoder();
        sut.Encode(103, new Http3ResponseHeaderCollection(), writer);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        byte[] expected = [2, 3, 0, 0, 24 | 0b1100_0000];
        Assert.True(expected.SequenceEqual(stream.ToArray()));
    }

    [Fact]
    public async Task EncodeLiteralFieldWithLiteralValue()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder.EncodeLiteralFieldWithLiteralValue("a", "b", pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x21, 0x61, 0x01, 0x62 }));
    }

    [Fact]
    public async Task EncodeLiteralFieldWithLiteralValue2()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder.EncodeLiteralFieldWithLiteralValue("ab", "ba", pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x22, 0x61, 0x62, 0x02, 0x62, 0x61 }));
    }

    [Fact]
    public async Task WritingIntoReusedMemory_EncodeLiteralFieldWithLiteralValue()
    {
        var pipe = new MemoryReusingPipeWriter();
        QPackDecoder.EncodeLiteralFieldWithLiteralValue("ab", "ba", pipe);
        pipe.Complete();
        Assert.True(pipe.WrittenData.ToArray().SequenceEqual(new byte[] { 0x22, 0x61, 0x62, 0x02, 0x62, 0x61 }));
    }

    [Fact]
    public async Task EncodeIndexedFieldWithLiteralValue()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        var header = new QPackDecoder.EncodingKnownHeaderField(1, "a");
        QPackDecoder.EncodeIndexedFieldWithLiteralValue(header, "b", pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x71, 0x01, 0x62 }));
    }

    [Fact]
    public async Task EncodeIndexedFieldWithLiteralValue1()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        var header = new EncodingKnownHeaderField(1, "a");
        QPackDecoder.EncodeIndexedFieldWithLiteralValue(header, "ab", pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x71, 0x02, 0x61, 0x62 }));
    }

    [Fact]
    public async Task WritingIntoReusedMemory_EncodeIndexedFieldWithLiteralValue()
    {
        // Test 1
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        var header = new EncodingKnownHeaderField(99, "a"); // 99 is important to be on 2 bytes
        QPackDecoder.EncodeIndexedFieldWithLiteralValue(header, "ab", pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x7F, 0x54, 0x02, 0x61, 0x62 }));

        // Test 2
        var reusingPipe = new MemoryReusingPipeWriter();
        QPackDecoder.EncodeIndexedFieldWithLiteralValue(header, "ab", reusingPipe);
        reusingPipe.Complete();
        Assert.True(reusingPipe.WrittenData.ToArray().SequenceEqual(new byte[] { 0x7F, 0x54, 0x02, 0x61, 0x62 }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EncodeIndexedFieldWithIndexedValue(byte index)
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        var header = new EncodingKnownHeaderField(index, string.Empty);
        QPackDecoder.EncodeIndexedFieldLine(header, pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual([(byte)(0xC0 + index)]));
    }

    [Fact]
    public async Task EncodeStatusCodeRaw200()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder sut = new();
        sut.Encode(200, [], pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0xD9 }));
    }

    [Fact]
    public async Task EncodeStatusCodeRaw8()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder sut = new();
        sut.Encode(8, [], pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x7F, 0x37, 0x01, 0x38 }));
    }

    [Fact]
    public async Task EncodeWithoutStatusCode()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder sut = new();
        sut.Encode([], pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x00, 0x00 }));
    }

    [Fact]
    public async Task EncodeStatusCodeAndHeaders()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder sut = new();
        Http3ResponseHeaderCollection headers = new()
        {
            { "a", "b" }, // name and value
            { "referer", "" }, // Static table index 13, name indexed
            { "cache-control", "no-cache" }, // name and value indexed
            { "content-encoding", "a" } // name indexed only
        };
        sut.Encode(200, headers, pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0xD9, 0x21, 0x61, 0x01, 0x62,
        
          // referer,  len,  cache-control, content-encoding, a-len, a
             0x7D,     0x00, 0xE7,          0x7F, 0x1B,      0x01,  0x61
        }));
    }

    [Fact]
    public async Task EncodeWithoutStatusCodeHeaders()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder sut = new();
        Http3ResponseHeaderCollection headers = new()
        {
            { "a", "b" }, // name and value
            { "referer", "" }, // Static table index 13, name indexed
            { "cache-control", "no-cache" }, // name and value indexed
            { "content-encoding", "a" } // name indexed only
        };
        sut.Encode(headers, pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0x21, 0x61, 0x01, 0x62,
        
          // referer,  len,  cache-control, content-encoding, a-len, a
             0x7D,     0x00, 0xE7,          0x7F, 0x1B,      0x01,  0x61
        }));
    }

    // A PipeWriter that does not clear the underlying buffer.
    private class MemoryReusingPipeWriter : PipeWriter
    {
        private byte[] _buffer = new byte[4096];
        private int _bufferLength = 0;

        public MemoryStream WrittenData { get; } = new MemoryStream();

        public override void Advance(int bytes)
        {
            WrittenData.Write(_buffer.AsSpan(0, bytes));
            _bufferLength = 0;
        }

        public override void CancelPendingFlush()
        {
        }

        public override void Complete(Exception? exception = null)
        {
            WrittenData.Seek(0, SeekOrigin.Begin);
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new FlushResult());
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (sizeHint > _buffer.Length - _bufferLength)
            {
                Array.Resize(ref _buffer, _bufferLength + sizeHint);
            }
            _buffer.AsSpan()[_bufferLength..].Fill(255);
            return _buffer.AsMemory(_bufferLength..);
        }

        public override Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
    }
}