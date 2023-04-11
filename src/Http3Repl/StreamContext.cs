using System.IO.Pipelines;
using System.Net.Http.QPack;
using System.Net.Quic;

internal class StreamContext
{
    public QPackDecoder? HeaderDecoder { get; set; }

    public required QuicStreamType StreamType { get; set; }

    public required PipeReader Reader { get; set; }

    public required ConnectionContext Connection { get; init; }

    public required QuicStream Stream { get; init; }
}
