using System.Buffers;
using System.Text;
using CHttpServer.Http3;
using Microsoft.Net.Http.Headers;

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
        Assert.Equal("sameorigin", testHandler.Headers[HeaderNames.XFrameOptions]);
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
        Assert.Equal("sameorigin", testHandler.Headers[HeaderNames.XFrameOptions]);
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
            Assert.Equal("sameorigin", testHandler.Headers[HeaderNames.XFrameOptions]);
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
        byte[] data = [0x00, 0x00, 0x24, 0x74, 0x65, 0x73, 0x74, 0x02, 0x6f, 0x6b, 0x27, 0x05, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x0b, 0x6c, 0x6f, 0x6e, 0x67, 0x65, 0x72, 0x76, 0x61, 0x6c, 0x75, 0x65];
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
        Assert.Equal("sameorigin", testHandler.Headers[HeaderNames.XFrameOptions]);
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
        Assert.Equal("sameorigin", testHandler.Headers[HeaderNames.XFrameOptions]);
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
            Assert.Equal("sameorigin", testHandler.Headers[HeaderNames.XFrameOptions]);
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
        Assert.Equal("sameorigin", testHandler.Headers[HeaderNames.XFrameOptions]);
        Assert.Equal("ok", testHandler.Headers["test"]);
        Assert.Equal("longervalue", testHandler.Headers["longerheader"]);
    }

    [Fact]
    public void Decode_ETag()
    {
        // 0, 0 - prefix, 0x07 - ETag, 0x40 - field line and name reference, 0x10 - static table, 0x01 - length, 0x61 - 'a'
        byte[] data = [0x00, 0x00, 0x07 + 0x40 + 0x10, 0x01, 0x61];
        for (int i = 1; i < data.Length - 1; i++)
        {
            QPackDecoder decoder = new();
            TestQPackHeaderHandler testHandler = new();
            var success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(0, i)), testHandler, out long consumed);
            success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data.AsMemory(checked((int)consumed))), testHandler, out long finalConsumed);
            Assert.True(success);
            Assert.Equal(data.Length, consumed + finalConsumed);
            Assert.Single(testHandler.Headers);
            Assert.Equal("a", testHandler.Headers[HeaderNames.ETag]);
        }
    }

    [Theory]
    [MemberData(nameof(KnownAspNetHeaders))]
    public void Decode_AspNetHeaders(string headerName, byte staticTableIndex)
    {
        // 0, 0 - prefix, table index, 0x40 - field line and name reference, 0x10 - static table, 0x01 - length, 0x61 - 'a'
        Span<byte> encodedIndex = new byte[8];
        int length = QPackIntegerEncoder.Encode(encodedIndex, staticTableIndex, 4);
        encodedIndex[0] += 0x40 + 0x10;

        byte[] data = [0x00, 0x00, .. encodedIndex[..length], 0x01, 0x61];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        var success = decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed);
        Assert.True(success);
        Assert.Single(testHandler.Headers);
        Assert.Equal("a", testHandler.Headers[headerName]);
    }

    public static IEnumerable<TheoryDataRow<string, byte>> KnownAspNetHeaders =>
    [
        new(HeaderNames.Age, 2),
        new(HeaderNames.ContentDisposition, 3),
        new(HeaderNames.ContentLength, 4),
        new(HeaderNames.Cookie, 5),
        new(HeaderNames.Date, 6),
        new(HeaderNames.ETag, 7),
        new(HeaderNames.IfModifiedSince, 8),
        new(HeaderNames.IfNoneMatch, 9),
        new(HeaderNames.LastModified, 10),
        new(HeaderNames.Link, 11),
        new(HeaderNames.Location, 12),
        new(HeaderNames.Referer, 13),
        new(HeaderNames.SetCookie, 14),
        new(HeaderNames.Accept, 29),
        new(HeaderNames.Accept, 30),
        new(HeaderNames.AcceptEncoding, 31),
        new(HeaderNames.AcceptRanges, 32),
        new(HeaderNames.AccessControlAllowHeaders, 33),
        new(HeaderNames.AccessControlAllowHeaders, 34),
        new(HeaderNames.AccessControlAllowOrigin, 35),
        new(HeaderNames.CacheControl, 36),
        new(HeaderNames.CacheControl, 37),
        new(HeaderNames.CacheControl, 38),
        new(HeaderNames.CacheControl, 39),
        new(HeaderNames.CacheControl, 40),
        new(HeaderNames.CacheControl, 41),
        new(HeaderNames.ContentEncoding, 42),
        new(HeaderNames.ContentEncoding, 43),
        new(HeaderNames.ContentType, 44),
        new(HeaderNames.ContentType, 45),
        new(HeaderNames.ContentType, 46),
        new(HeaderNames.ContentType, 47),
        new(HeaderNames.ContentType, 48),
        new(HeaderNames.ContentType, 49),
        new(HeaderNames.ContentType, 50),
        new(HeaderNames.ContentType, 51),
        new(HeaderNames.ContentType, 52),
        new(HeaderNames.ContentType, 53),
        new(HeaderNames.ContentType, 54),
        new(HeaderNames.Range, 55),
        new(HeaderNames.StrictTransportSecurity, 56),
        new(HeaderNames.StrictTransportSecurity, 57),
        new(HeaderNames.StrictTransportSecurity, 58),
        new(HeaderNames.Vary, 59),
        new(HeaderNames.Vary, 60),
        new(HeaderNames.XContentTypeOptions, 61),
        new(HeaderNames.XXSSProtection, 62),
        new(HeaderNames.AcceptLanguage, 72),
        new(HeaderNames.AccessControlAllowCredentials, 73),
        new(HeaderNames.AccessControlAllowCredentials, 74),
        new(HeaderNames.AccessControlAllowHeaders, 75),
        new(HeaderNames.AccessControlAllowMethods, 76),
        new(HeaderNames.AccessControlAllowMethods, 77),
        new(HeaderNames.AccessControlAllowMethods, 78),
        new(HeaderNames.AccessControlExposeHeaders, 79),
        new(HeaderNames.AccessControlRequestHeaders, 80),
        new(HeaderNames.AccessControlRequestMethod, 81),
        new(HeaderNames.AccessControlRequestMethod, 82),
        new(HeaderNames.AltSvc, 83),
        new(HeaderNames.Authorization, 84),
        new(HeaderNames.ContentSecurityPolicy, 85),
        new(HeaderNames.IfRange, 89),
        new(HeaderNames.Origin, 90),
        new(HeaderNames.Server, 92),
        new(HeaderNames.UpgradeInsecureRequests, 94),
        new(HeaderNames.UserAgent, 95),
        new(HeaderNames.XFrameOptions, 97),
        new(HeaderNames.XFrameOptions, 98),
    ];


    private class TestQPackHeaderHandler : IQPackHeaderHandler
    {
        internal Dictionary<string, string> Headers { get; } = new();

        public void OnHeader(in KnownHeaderField name, in ReadOnlySequence<byte> value)
        {
            Headers.Add(name.Name, Encoding.Latin1.GetString(value.ToArray()));
        }

        public void OnHeader(in KnownHeaderField staticHeader)
        {
            Headers.Add(staticHeader.Name, staticHeader.Value);
        }

        public void OnHeader(in ReadOnlySequence<byte> fieldName, in ReadOnlySequence<byte> fieldValue)
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
