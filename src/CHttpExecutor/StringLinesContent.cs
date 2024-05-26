using System.IO.Pipelines;
using System.Net;
using System.Text;

namespace CHttpExecutor;

internal class StringLinesContent(IEnumerable<string> content) : HttpContent
{
    private readonly IEnumerable<string> _content = content;

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var pipe = PipeWriter.Create(stream);
        foreach (var segment in _content)
            Encoding.UTF8.GetBytes(segment, pipe);
        await pipe.FlushAsync();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        foreach (var segment in _content)
            length += Encoding.UTF8.GetByteCount(segment);
        return true;
    }
}