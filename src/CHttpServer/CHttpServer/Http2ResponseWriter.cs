using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer;

internal class Http2ResponseWriter
{
    private readonly DynamicHPackEncoder _hpackEncoder;
    private readonly FrameWriter _frameWriter;

    public Http2ResponseWriter(FrameWriter frameWriter)
    {
        _frameWriter = frameWriter;
        _hpackEncoder = new DynamicHPackEncoder();
    }
}
