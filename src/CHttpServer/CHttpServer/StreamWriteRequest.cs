namespace CHttpServer;

internal record struct StreamWriteRequest(Http2Stream Stream, string OperationName, ulong Data = 0);
