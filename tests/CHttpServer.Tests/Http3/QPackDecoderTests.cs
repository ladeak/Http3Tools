using System.Buffers;
using System.Text;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class QPackDecoderTests
{
    [Fact]
    public void Decode_IndexFieldLineWithNameReference()
    {
        // Required Insert Count = 0, Base = 0
        // 510b 2f69 6e64 6578 | Literal Field Line with Name Reference
        // 2e68 746d 6c | Static Table, Index = 1
        // :path =/ index.html
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        var success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed);
        Assert.True(success);
        Assert.Equal(data.Length, consumed);
        Assert.Single(testHandler.Headers);
        Assert.Equal("/index.html", testHandler.Headers[":path"]);
    }

    [Fact]
    public void Decode_IndexFieldLineWithNameReference_ReadOnlySequenceBoundaries()
    {
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c];
        var input = new MemorySegment<byte>(data.AsMemory(0, 1));
        for (int i = 1; i < data.Length; i++)
            input = input.Append(data.AsMemory(i, 1));
        var success = decoder.DecodeHeader(input.AsSequence(), testHandler, out long consumed);
        Assert.True(success);
        Assert.Equal(data.Length, consumed);
        Assert.Single(testHandler.Headers);
        Assert.Equal("/index.html", testHandler.Headers[":path"]);
    }

    [Fact]
    public void Decode_IndexFieldLineWithNameReference_PartialData()
    {
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c];
        for (int i = 1; i < data.Length - 1; i++)
        {
            QPackDecoder decoder = new();
            TestQPackHeaderHandler testHandler = new();
            var success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(0, i)), testHandler, out long consumed);
            success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(checked((int)consumed))), testHandler, out long finalConsumed);
            Assert.True(success);
            Assert.Equal(data.Length, consumed + finalConsumed);
            Assert.Single(testHandler.Headers);
            Assert.Equal("/index.html", testHandler.Headers[":path"]);
        }
    }

    [Fact]
    public void Decode_IndexFieldLine()
    {
        // Required Insert Count = 0, Base = 0
        // :method GET, :scheme https, :status 200, x-frame-options sameorigin
        byte[] data = [0x00, 0x00, 0xd1, 0xd7, 0xd9, 0xFF, 0x23];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed);
        Assert.Equal(data.Length, consumed);
        Assert.Equal(4, testHandler.Headers.Count);
        Assert.Equal("GET", testHandler.Headers[":method"]);
        Assert.Equal("https", testHandler.Headers[":scheme"]);
        Assert.Equal("200", testHandler.Headers[":status"]);
        Assert.Equal("sameorigin", testHandler.Headers["x-frame-options"]);
    }

    [Fact]
    public void Decode_IndexFieldLine_ReadOnlySequenceBoundaries()
    {
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        byte[] data = [0x00, 0x00, 0xd1, 0xd7, 0xd9, 0xFF, 0x23];
        var input = new MemorySegment<byte>(data.AsMemory(0, 1));
        for (int i = 1; i < data.Length; i++)
            input = input.Append(data.AsMemory(i, 1));
        var success = decoder.DecodeHeader(input.AsSequence(), testHandler, out long consumed);
        Assert.True(success);
        Assert.Equal(4, testHandler.Headers.Count);
        Assert.Equal("GET", testHandler.Headers[":method"]);
        Assert.Equal("https", testHandler.Headers[":scheme"]);
        Assert.Equal("200", testHandler.Headers[":status"]);
        Assert.Equal("sameorigin", testHandler.Headers["x-frame-options"]);
    }

    [Fact]
    public void Decode_IndexFieldLine_PartialData()
    {
        byte[] data = [0x00, 0x00, 0xd1, 0xd7, 0xd9, 0xFF, 0x23];
        for (int i = 1; i < data.Length - 1; i++)
        {
            QPackDecoder decoder = new();
            TestQPackHeaderHandler testHandler = new();
            var success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(0, i)), testHandler, out long consumed);
            success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(checked((int)consumed))), testHandler, out long finalConsumed);
            Assert.True(success);
            Assert.Equal(4, testHandler.Headers.Count);
            Assert.Equal("GET", testHandler.Headers[":method"]);
            Assert.Equal("https", testHandler.Headers[":scheme"]);
            Assert.Equal("200", testHandler.Headers[":status"]);
            Assert.Equal("sameorigin", testHandler.Headers["x-frame-options"]);
        }
    }

    [Fact]
    public void Decode_IndexFieldLine_PostBaseIndex()
    {
        byte[] data = [0x00, 0x00, 0x11];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        Assert.Throws<HeaderDecodingException>(() => decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed));
    }

    [Fact]
    public void Decode_IndexFieldLineWithNameReference_PostBaseIndex()
    {
        byte[] data = [0x00, 0x00, 0x0F];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        Assert.Throws<HeaderDecodingException>(() => decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed));
    }

    [Fact]
    public void Decode_LiteralFieldLineLiteralName()
    {
        // Required Insert Count = 0, Base = 0
        // test: ok, longerheader: longervalue
        //                         length t     e     s     t    length   o     k  length         l     o     n     g     e     r     h     e     a     d     e     r  length   l     o     n     g     e     r     v     a     l     u     e
        byte[] data = [0x00, 0x00, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65 ];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed);
        Assert.Equal(data.Length, consumed);
        Assert.Equal(2, testHandler.Headers.Count);
        Assert.Equal("ok", testHandler.Headers["test"]);
        Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
    }

    [Fact]
    public void Decode_LiteralFieldLineLiteralName_ReadOnlySequenceBoundaries()
    {
        // Required Insert Count = 0, Base = 0
        // test: ok, longerheader: longervalue
        //                         length t     e     s     t    length   o     k  length         l     o     n     g     e     r     h     e     a     d     e     r  length   l     o     n     g     e     r     v     a     l     u     e
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        byte[] data = [0x00, 0x00, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65];
        var input = new MemorySegment<byte>(data.AsMemory(0, 1));
        for (int i = 1; i < data.Length; i++)
            input = input.Append(data.AsMemory(i, 1));
        var success = decoder.DecodeHeader(input.AsSequence(), testHandler, out long consumed);
        Assert.True(success);
        Assert.Equal(2, testHandler.Headers.Count);
        Assert.Equal("ok", testHandler.Headers["test"]);
        Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
    }

    [Fact]
    public void Decode_LiteralFieldLineLiteralName_PartialData()
    { 
        // Required Insert Count = 0, Base = 0
        // test: ok, longerheader: longervalue
        //                         length t     e     s     t    length   o     k  length         l     o     n     g     e     r     h     e     a     d     e     r  length   l     o     n     g     e     r     v     a     l     u     e
        byte[] data = [0x00, 0x00, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65];
        for (int i = 1; i < data.Length - 1; i++)
        {
            QPackDecoder decoder = new();
            TestQPackHeaderHandler testHandler = new();
            var success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(0, i)), testHandler, out long consumed);
            success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(checked((int)consumed))), testHandler, out long finalConsumed);
            Assert.True(success);
            Assert.Equal(2, testHandler.Headers.Count);
            Assert.Equal("ok", testHandler.Headers["test"]);
            Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
        }
    }

    [Fact]
    public void Decode_Mix()
    {
        // Required Insert Count = 0, Base = 0
        // :path =/ index.html, :method GET, :scheme https, :status 200, x-frame-options: sameorigin, test: ok, longerheader: longervalue
        //                                                                                                             https       x-frame    length t     e     s     t    length   o     k  length         l     o     n     g     e     r     h     e     a     d     e     r  length   l     o     n     g     e     r     v     a     l     u     e
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c, 0xd1, 0xd7, 0xd9, 0xFF, 0x23, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed);
        Assert.Equal(data.Length, consumed);
        Assert.Equal(7, testHandler.Headers.Count);
        Assert.Equal("/index.html", testHandler.Headers[":path"]);
        Assert.Equal("GET", testHandler.Headers[":method"]);
        Assert.Equal("https", testHandler.Headers[":scheme"]);
        Assert.Equal("200", testHandler.Headers[":status"]);
        Assert.Equal("sameorigin", testHandler.Headers["x-frame-options"]);
        Assert.Equal("ok", testHandler.Headers["test"]);
        Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
    }

    [Fact]
    public void Decode_Mix_ReadOnlySequenceBoundaries()
    {
        // Required Insert Count = 0, Base = 0
        // :path =/ index.html, :method GET, :scheme https, :status 200, x-frame-options: sameorigin, test: ok, longerheader: longervalue
        //                                                                                                             https       x-frame    length t     e     s     t    length   o     k  length         l     o     n     g     e     r     h     e     a     d     e     r  length   l     o     n     g     e     r     v     a     l     u     e
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c, 0xd1, 0xd7, 0xd9, 0xFF, 0x23, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        var input = new MemorySegment<byte>(data.AsMemory(0, 1));
        for (int i = 1; i < data.Length; i++)
            input = input.Append(data.AsMemory(i, 1));
        var success = decoder.DecodeHeader(input.AsSequence(), testHandler, out long consumed);
        Assert.True(success);
        Assert.Equal(7, testHandler.Headers.Count);
        Assert.Equal("/index.html", testHandler.Headers[":path"]);
        Assert.Equal("GET", testHandler.Headers[":method"]);
        Assert.Equal("https", testHandler.Headers[":scheme"]);
        Assert.Equal("200", testHandler.Headers[":status"]);
        Assert.Equal("sameorigin", testHandler.Headers["x-frame-options"]);
        Assert.Equal("ok", testHandler.Headers["test"]);
        Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
    }

    [Fact]
    public void Decode_Mix_PartialData()
    {
        // Required Insert Count = 0, Base = 0
        // :path =/ index.html, :method GET, :scheme https, :status 200, x-frame-options: sameorigin, test: ok, longerheader: longervalue
        //                                                                                                             https       x-frame    length t     e     s     t    length   o     k  length         l     o     n     g     e     r     h     e     a     d     e     r  length   l     o     n     g     e     r     v     a     l     u     e
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c, 0xd1, 0xd7, 0xd9, 0xFF, 0x23, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65];
        for (int i = 1; i < data.Length - 1; i++)
        {
            QPackDecoder decoder = new();
            TestQPackHeaderHandler testHandler = new();
            var success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(0, i)), testHandler, out long consumed);
            success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(checked((int)consumed))), testHandler, out long finalConsumed);
            Assert.True(success);
            Assert.Equal(7, testHandler.Headers.Count);
            Assert.Equal("/index.html", testHandler.Headers[":path"]);
            Assert.Equal("GET", testHandler.Headers[":method"]);
            Assert.Equal("https", testHandler.Headers[":scheme"]);
            Assert.Equal("200", testHandler.Headers[":status"]);
            Assert.Equal("sameorigin", testHandler.Headers["x-frame-options"]);
            Assert.Equal("ok", testHandler.Headers["test"]);
            Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
        }
    }

    [Fact]
    public void Decode_Mix_DifferentHeaderOrder()
    {
        // Required Insert Count = 0, Base = 0
        // :path =/ index.html, :method GET, :status 200, x-frame-options: sameorigin, test: ok, longerheader: longervalue, :scheme https,
        //                                                                                                                               length t     e     s     t    length   o     k  length         l     o     n     g     e     r     h     e     a     d     e     r  length   l     o     n     g     e     r     v     a     l     u     e  https
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c, 0xd1, 0xd9, 0xFF, 0x23, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65, 0xd7,];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed);
        Assert.Equal(data.Length, consumed);
        Assert.Equal(7, testHandler.Headers.Count);
        Assert.Equal("/index.html", testHandler.Headers[":path"]);
        Assert.Equal("GET", testHandler.Headers[":method"]);
        Assert.Equal("https", testHandler.Headers[":scheme"]);
        Assert.Equal("200", testHandler.Headers[":status"]);
        Assert.Equal("sameorigin", testHandler.Headers["x-frame-options"]);
        Assert.Equal("ok", testHandler.Headers["test"]);
        Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
    }

    private class TestQPackHeaderHandler : IQPackHeaderHandler
    {
        internal Dictionary<string, string> Headers { get; } = new();

        public void OnHeader(byte[] name, ReadOnlySequence<byte> value)
        {
            Headers.Add(Encoding.Latin1.GetString(name), Encoding.Latin1.GetString(value.ToArray()));
        }

        public void OnHeader(HeaderField staticHeader)
        {
            Headers.Add(Encoding.Latin1.GetString(staticHeader.Name), Encoding.Latin1.GetString(staticHeader.Value));
        }

        public void OnHeader(ReadOnlySequence<byte> fieldName, ReadOnlySequence<byte> fieldValue)
        {
            Headers.Add(Encoding.Latin1.GetString(fieldName.ToArray()), Encoding.Latin1.GetString(fieldValue.ToArray()));
        }
    }

    public class MemorySegment<T> : ReadOnlySequenceSegment<T>
    {
        public MemorySegment<T> Head { get; init; }

        public MemorySegment(ReadOnlyMemory<T> memory)
        {
            Memory = memory;
            Head = this;
        }

        public MemorySegment<T> Append(ReadOnlyMemory<T> memory)
        {
            var segment = new MemorySegment<T>(memory)
            {
                Head = this.Head,
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;

            return segment;
        }

        public ReadOnlySequence<T> AsSequence()
        {
            return new ReadOnlySequence<T>(Head, 0, this, Memory.Length);
        }

        public MemorySegment<T>? NextSegment => Next as MemorySegment<T>;
    }
}
