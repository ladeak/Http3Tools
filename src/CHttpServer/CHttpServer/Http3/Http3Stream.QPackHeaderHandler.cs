using System.Buffers;

namespace CHttpServer.Http3;

internal sealed partial class Http3Stream : IQPackHeaderHandler
{
    public void OnHeader(byte[] name, ReadOnlySequence<byte> value)
    {
    }

    public void OnHeader(HeaderField staticHeader)
    {
    }

    public void OnHeader(ReadOnlySequence<byte> fieldName, ReadOnlySequence<byte> fieldValue)
    {
    }
}
