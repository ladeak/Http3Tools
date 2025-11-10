using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml.Linq;
using CHttpServer.Http3;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CHttpServer.Tests.Http3;

public class QPackDecoderTests
{
    [Fact]
    public void Decode_IndexFieldLine()
    {
        // Required Insert Count = 0, Base = 0
        // 510b 2f69 6e64 6578 | Literal Field Line with Name Reference
        // 2e68 746d 6c | Static Table, Index = 1
        // | (: path =/ index.html)
        byte[] data = [0x00, 0x00, 0x51, 0x0b, 0x2f, 0x69, 0x6e, 0x64, 0x65, 0x78, 0x2e, 0x68, 0x74, 0x6d, 0x6c];
        QPackDecoder decoder = new();
        TestQPackHeaderHandler testHandler = new();
        decoder.DecodeHeader(new ReadOnlySequence<byte>(data), testHandler, out long consumed);
        Assert.Equal(data.Length, consumed);
        Assert.Single(testHandler.Headers);
        Assert.Equal("/index.html", testHandler.Headers[":path"]);
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
}
