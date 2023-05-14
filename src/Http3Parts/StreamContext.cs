using System.IO.Pipelines;
using System.Net.Http.QPack;
using System.Net.Quic;

public class StreamContext
{
    internal QPackDecoder? HeaderDecoder { get; set; }

    public PipeReader? Reader { get; set; }

    public required ConnectionContext Connection { get; init; }

    public required QuicStream Stream { get; init; }

    public required bool Incoming { get; init; }

    internal Http3StreamType? StreamType { get; set; }

    public bool InitialFrameProcessed { get; set; }

    internal Dictionary<Http3SettingType, long> Settings { get; } = new();
}
