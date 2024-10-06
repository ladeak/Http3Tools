namespace CHttpServer;

internal sealed partial class Http2Connection : System.Net.Http.HPack.IHttpStreamHeadersHandler
{
    public void OnDynamicIndexedHeader(int? index, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
    }

    public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
    }

    public void OnHeadersComplete(bool endStream)
    {
    }

    public void OnStaticIndexedHeader(int index)
    {
    }

    public void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
    {
    }
}