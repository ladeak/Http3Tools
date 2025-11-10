using System.Buffers;

namespace CHttpServer.Http3;

internal interface IQPackHeaderHandler
{
    internal void OnHeader(byte[] name, ReadOnlySequence<byte> value);

    internal void OnHeader(HeaderField staticHeader);

    internal void OnHeader(ReadOnlySequence<byte> fieldName, ReadOnlySequence<byte> fieldValue);
}