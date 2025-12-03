using System.Buffers;

namespace CHttpServer.Http3;

internal interface IQPackHeaderHandler
{
    internal void OnHeader(in KnownHeaderField staticHeader, in ReadOnlySequence<byte> value);

    internal void OnHeader(in KnownHeaderField staticHeader);

    internal void OnHeader(in ReadOnlySequence<byte> fieldName, in ReadOnlySequence<byte> fieldValue);
}