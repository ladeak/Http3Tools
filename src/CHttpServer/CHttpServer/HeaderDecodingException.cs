namespace CHttpServer;

internal sealed class HeaderDecodingException : Exception
{
    public HeaderDecodingException()
    {
    }

    public HeaderDecodingException(string message) : base(message)
    {
    }

    public HeaderDecodingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
