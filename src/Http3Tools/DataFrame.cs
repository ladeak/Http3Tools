using System.Buffers;

internal struct DataFrame
{
    public required Http3FrameType? Type { get; init; }

    public required ReadOnlySequence<byte> Payload { get; set; }
}
