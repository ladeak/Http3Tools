using System.Text;

namespace CHttpServer;

public static class HttpStaticFieldParser
{
    private const string Get = "GET";
    private const string Put = "PUT";
    private const string Post = "POST";
    private const string Delete = "DELETE";
    private const string Head = "HEAD";
    private const string Options = "OPTIONS";
    private const string Connect = "CONNECT";
    private const string Trace = "TRACE";
    private const string Patch = "PATCH";
    private const string Https = "https";

    public static string GetMethod(ReadOnlySpan<byte> method)
    {
        switch (method.Length)
        {
            case 3:
                if ("GET"u8.SequenceEqual(method))
                    return Get;
                if ("PUT"u8.SequenceEqual(method))
                    return Put;
                break;
            case 4:
                if ("POST"u8.SequenceEqual(method))
                    return Post;
                if ("HEAD"u8.SequenceEqual(method))
                    return Head;
                break;
            case 5:
                if ("PATCH"u8.SequenceEqual(method))
                    return Patch;
                if ("TRACE"u8.SequenceEqual(method))
                    return Trace;
                break;
            case 6:
                if ("DELETE"u8.SequenceEqual(method))
                    return Delete;
                break;
            case 7:
                if ("OPTIONS"u8.SequenceEqual(method))
                    return Options;
                if ("CONNECT"u8.SequenceEqual(method))
                    return Connect;
                break;
        }
        return Encoding.Latin1.GetString(method);
    }

    public static string GetScheme(ReadOnlySpan<byte> scheme)
    {
        if ("https"u8.SequenceEqual(scheme))
            return Https;
        return Encoding.Latin1.GetString(scheme);
    }
}

