namespace CHttpServer;

/// <remarks>
/// From https://tools.ietf.org/html/rfc7540#section-4.1:
///    +-----------------------------------------------+
///    |                 Length (24)                   |
///    +---------------+---------------+---------------+
///    |   Type (8)    |   Flags (8)   |
///    +-+-------------+---------------+-------------------------------+
///    |R|                 Stream Identifier (31)                      |
///    +=+=============================================================+
///    |                   Frame Payload (0...)                      ...
///    +---------------------------------------------------------------+
/// </remarks>
internal class Http2Frame
{
    public uint PayloadLength { get; set; }

    public Http2FrameType Type { get; set; }

    public byte Flags { get; set; }

    public uint StreamId { get; set; }

    // GoAway

    public int GoAwayLastStreamId { get; set; }

    public Http2ErrorCode GoAwayErrorCode { get; set; }

    public void SetGoAway(int lastStreamId, Http2ErrorCode errorCode)
    {
        PayloadLength = 8;
        Type = Http2FrameType.GOAWAY;
        Flags = 0;
        StreamId = 0;
        GoAwayLastStreamId = lastStreamId;
        GoAwayErrorCode = errorCode;
    }

    // Settings

    public void SetSettingsAck()
    {
        PayloadLength = 0;
        Type = Http2FrameType.SETTINGS;
        Flags = 1;
        StreamId = 0;
    }

    public void SetSettings(uint size)
    {
        Type = Http2FrameType.SETTINGS;
        Flags = 0;
        StreamId = 0;
        PayloadLength = size;
    }
}
