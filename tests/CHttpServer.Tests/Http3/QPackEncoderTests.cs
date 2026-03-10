using System.IO.Pipelines;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class QPackEncoderTests
{
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
    public async Task EncodeIndexedFieldWithLiteralValue()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        var header = new KnownHeaderField(1, "a", Array.Empty<byte>(), string.Empty, Array.Empty<byte>());
        QPackDecoder.EncodeIndexedFieldWithLiteralValue(header, "b", pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x71, 0x01, 0x62 }));
    }

    [Fact]
    public async Task EncodeIndexedFieldWithLiteralValue1()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        var header = new KnownHeaderField(1, "a", Array.Empty<byte>(), string.Empty, Array.Empty<byte>());
        QPackDecoder.EncodeIndexedFieldWithLiteralValue(header, "ab", pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x71, 0x02, 0x61, 0x62 }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EncodeIndexedFieldWithIndexedValue(byte index)
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        var header = new KnownHeaderField(index, "a", Array.Empty<byte>(), "ab", Array.Empty<byte>());
        QPackDecoder.EncodeIndexedFieldLine(header, pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual([(byte)(0xC0 + index)]));
    }

    [Fact]
    public async Task EncodeStatusCode()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        QPackDecoder sut = new();
        sut.Encode(200, [], pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0xD9 }));
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
        Http3ResponseHeaderCollection headers = new();
        headers.Add("a", "b"); // name and value
        headers.Add("referer", ""); // Static table index 13, name indexed
        headers.Add("cache-control", "no-cache"); // name and value indexed
        headers.Add("content-encoding", "a"); // name indexed only
        sut.Encode(200, headers, pipe);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x00, 0x00, 0xD9, 0x21, 0x61, 0x01, 0x62,
        
          // referer,  len,  cache-control, content-encoding, a-len, a
             0x7D,     0x00, 0xE7,          0x7F, 0x1B,      0x01,  0x61
        }));
    }
}
