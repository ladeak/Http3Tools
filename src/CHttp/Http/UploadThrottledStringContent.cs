using System.Net;
using System.Text;
using CHttp.Abstractions;

namespace CHttp.Http;

internal class UploadThrottledStringContent : HttpContent
{
	private const int IntervalSizeMs = 15;
	private readonly ReadOnlyMemory<byte> _content;
	private readonly int _singleWriteSize;
	private readonly TimeSpan Interval = TimeSpan.FromMilliseconds(IntervalSizeMs);
	private readonly IAwaiter _awaiter;

	internal UploadThrottledStringContent(ReadOnlySpan<char> content, int kbytesec, IAwaiter awaiter)
	{
		_content = GetContentByteArray(content, Encoding.UTF8);
		_singleWriteSize = (int)Math.Ceiling((kbytesec * 1000.0) * IntervalSizeMs / 1000.0);
		_awaiter = awaiter ?? throw new ArgumentNullException(nameof(awaiter));
	}

	protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
	{
		var remainingContent = _content;
		while (remainingContent.Length > 0)
		{
			var segmentSize = remainingContent.Length > _singleWriteSize ? _singleWriteSize : remainingContent.Length;
			await stream.WriteAsync(remainingContent.Slice(0, segmentSize));
			remainingContent = remainingContent.Slice(segmentSize);
			if (remainingContent.Length > 0)
				await _awaiter.WaitAsync(Interval);
		}
	}

	protected override bool TryComputeLength(out long length)
	{
		length = _content.Length;
		return true;
	}

	private static ReadOnlyMemory<byte> GetContentByteArray(ReadOnlySpan<char> content, Encoding encoding)
	{
		var buffer = new byte[Encoding.UTF8.GetMaxByteCount(content.Length)];
		int length = encoding.GetBytes(content, buffer);
		return buffer.AsMemory(0,length);
	}
}