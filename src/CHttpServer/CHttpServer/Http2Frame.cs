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
    private const byte EndStreamFlag = 0x01;
    private const byte EndHeadersFlag = 0x04;

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

    public void SetResponseHeaders(uint streamId, int payloadLength)
    {
        Type = Http2FrameType.HEADERS;
        Flags = 0;
        StreamId = streamId;
        PayloadLength = (uint)payloadLength;
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

    // Headers
    public bool EndStream { get => (Flags & EndStreamFlag) != 0; set => Flags |= EndStreamFlag; }

    public bool EndHeaders { get => (Flags & EndHeadersFlag) != 0; set => Flags |= EndHeadersFlag; }

    public bool HasPadding => (Flags & 0x08) != 0;

    public bool HasPriorty => (Flags & 0x20) != 0;
}
