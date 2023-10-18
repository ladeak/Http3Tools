using System.Net;

namespace CHttp.Http;

public class MemoryArrayContent : HttpContent
{
    public readonly Memory<byte> _content;

    public MemoryArrayContent(Memory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
    }

	public MemoryArrayContent(MemoryArrayContent input)
	{
		ArgumentNullException.ThrowIfNull(input);
        _content = input._content;
	}

	protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        stream.Write(_content.Span);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsyncCore(stream, default).AsTask();

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        SerializeToStreamAsyncCore(stream, cancellationToken).AsTask();

    private protected ValueTask SerializeToStreamAsyncCore(Stream stream, CancellationToken cancellationToken) =>
        stream.WriteAsync(_content, cancellationToken);

    protected override bool TryComputeLength(out long length)
    {
        length = _content.Length;
        return true;
    }
}
