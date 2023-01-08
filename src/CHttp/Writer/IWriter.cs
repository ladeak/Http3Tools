using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

internal interface IWriter
{
    PipeWriter Pipe { get; }

    Task InitializeResponse(long totalSize, string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel);

    void WriteSummary(Summary summary);

    void WriteUpdate(Update update);
}