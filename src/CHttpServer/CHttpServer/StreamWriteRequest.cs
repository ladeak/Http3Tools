namespace CHttpServer;

internal record struct StreamWriteRequest(Http2Stream H2Stream, string OperationName, ulong Data = 0);
