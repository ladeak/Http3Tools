namespace CHttpServer;

internal sealed class Http2ConnectionException : Exception
{
    public Http2ConnectionException(string message) : base(message)
    {
    }

    public Http2ConnectionException(Http2ErrorCode code) : base()
    {
        Code = code;
    }

    public Http2ErrorCode? Code { get; }
}
