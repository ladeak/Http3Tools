using System.IO.Pipelines;
using System.Net.Http.QPack;
using System.Net.Quic;

internal class StreamContext
{
    public QPackDecoder? HeaderDecoder { get; set; }

    public required PipeReader Reader { get; set; }

    public required ConnectionContext Connection { get; init; }

    public required QuicStream Stream { get; init; }

    public required bool Incoming { get; init; }

    public Http3StreamType StreamType { get; set; }

    public bool InitialFrameProcessed { get; set; }

    public Dictionary<Http3SettingType, long> Settings { get; } = new();
}
