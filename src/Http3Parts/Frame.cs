namespace Http3Parts;

public class Frame
{
    public static Frame Default { get; } = new Frame() { Data = string.Empty, SourceStream = string.Empty };

    public required string SourceStream { get; init; }

    public required string Data { get; init; }
}
