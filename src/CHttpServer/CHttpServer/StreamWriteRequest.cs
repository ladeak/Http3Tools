namespace CHttpServer;

internal record class StreamWriteRequest(Http2Stream Stream, string OperationName, ulong Data = 0);
