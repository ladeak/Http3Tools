namespace CHttpServer.Http3;

internal class Http3ConnectionException : Exception
{
    public Http3ConnectionException(int errorCode) : base()
    {
        ErrorCode = errorCode;
    }
    public int ErrorCode { get; }
}
