namespace CHttpServer.Http3;

internal readonly struct Http3Settings
{
    public ulong? ServerMaxFieldSectionSize { get; init; }
}