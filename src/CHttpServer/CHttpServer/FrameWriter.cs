
using System.IO.Pipelines;

namespace CHttpServer;

public sealed class FrameWriter
{
    private readonly CHttpConnectionContext _context;
    private readonly PipeWriter _destination;

    public FrameWriter(CHttpConnectionContext context)
    {
        _context = context;
        _destination = context.TransportPipe.Output;
    }

    internal void WriteGoAway(int lastStreamId, Http2ErrorCode errorCode)
    {
        var frame = new Http2Frame();
        frame.GoAway(lastStreamId, errorCode);

    }
}
