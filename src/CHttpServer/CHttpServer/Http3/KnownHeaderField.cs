namespace CHttpServer.Http3;

internal readonly record struct KnownHeaderField(int StaticTableIndex, string Name, string Value);