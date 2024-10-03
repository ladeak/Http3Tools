namespace CHttpServer;

public sealed class Http2ConnectionException : Exception
{
    public Http2ConnectionException(string message) : base(message)
    {
    }
}
