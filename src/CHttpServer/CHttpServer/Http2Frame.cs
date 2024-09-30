namespace CHttpServer;

/// <summary>
/// Represents an HTTP/2 frame. The expected use pattern is that it will be instantiated once
/// and then, each time a frame is received or sent, it is reset with a PrepareX method.
/// This type is not responsible for binary serialization or deserialization.
/// </summary>
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
    public int PayloadLength { get; set; }

    public Http2FrameType Type { get; set; }

    public byte Flags { get; set; }

    public int StreamId { get; set; }

    // GoAway

    public int GoAwayLastStreamId { get; set; }

    public Http2ErrorCode GoAwayErrorCode { get; set; }

    public void GoAway(int lastStreamId, Http2ErrorCode errorCode)
    {
        PayloadLength = 8;
        Type = Http2FrameType.GOAWAY;
        Flags = 0;
        StreamId = 0;
        GoAwayLastStreamId = lastStreamId;
        GoAwayErrorCode = errorCode;
    }

    public void SettingsAck(Http2ErrorCode errorCode)
    {
        PayloadLength = 0;
        Type = Http2FrameType.SETTINGS;
        Flags = 1;
        StreamId = 0;
    }

    public void Settings(int lastStreamId, Http2ErrorCode errorCode)
    {
        Type = Http2FrameType.SETTINGS;
        Flags = 0;
        StreamId = 0;
    }
}
