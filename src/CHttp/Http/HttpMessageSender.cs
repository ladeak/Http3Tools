using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;

internal sealed class HttpMessageSender
{
    private readonly IWriter _writer;
    private readonly HttpClient _client;
    private readonly HttpBehavior _behavior;

    public HttpMessageSender(IWriter writer, HttpBehavior behavior)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        var messageHandler = new SocketsHttpHandler();
        messageHandler.MaxConnectionsPerServer = 1;
        messageHandler.AllowAutoRedirect = behavior.EnableRedirects;
        messageHandler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions()
        {
            // TODO: sockets behavior
        };

        if (!behavior.EnableCertificateValidation)
            messageHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        _client = new HttpClient(messageHandler);
        _client.Timeout = TimeSpan.FromSeconds(behavior.Timeout);
    }

    public async Task SendRequestAsync(HttpRequestDetails requestData)
    {
        var request = new HttpRequestMessage(requestData.Method, requestData.Uri);
        request.Version = requestData.Version;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Content = requestData.Content;
        SetHeaders(requestData, request);
        await SendRequest(_client, request);
    }

    private async Task SendRequest(HttpClient client, HttpRequestMessage request)
    {
        Summary summary = new Summary(request.RequestUri?.ToString() ?? string.Empty);
        HttpResponseHeaders? trailers = null;
        {
            try
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var charSet = response.Content.Headers.ContentType?.CharSet;
                var encoding = charSet is { } ? Encoding.GetEncoding(charSet) : Encoding.UTF8;
                await _writer.InitializeResponseAsync(response.StatusCode, response.Headers, response.Version, encoding);
                await ProcessResponseAsync(response, encoding);
                summary.RequestCompleted(response.StatusCode);
                trailers = response.TrailingHeaders;
            }
            catch (HttpRequestException requestException)
            {
                summary = summary with { Error = $"Request Error {requestException.Message}", ErrorCode = ErrorType.HttpRequestException };
            }
            catch (HttpProtocolException protocolException)
            {
                summary = summary with { Error = $"Protocol Error {protocolException.ErrorCode}", ErrorCode = ErrorType.HttpProtocolException };
            }
            catch (OperationCanceledException)
            {
                summary = summary with { Error = "Request Timed Out", ErrorCode = ErrorType.Timeout };
            }
            catch (Exception ex)
            {
                summary = summary with { Error = $"Generic Error {ex}", ErrorCode = ErrorType.Other };
            }
        }
        await _writer.WriteSummaryAsync(trailers, summary);
    }

    private async Task ProcessResponseAsync(HttpResponseMessage response, Encoding encoding)
    {
        var contentStream = await response.Content.ReadAsStreamAsync();
        var transcodingStream = _behavior.ToUtf8 ? Encoding.CreateTranscodingStream(contentStream, encoding, Encoding.UTF8) : contentStream;
        await transcodingStream.CopyToAsync(_writer.Buffer);
        await _writer.Buffer.CompleteAsync();
    }

    private void SetHeaders(HttpRequestDetails requestData, HttpRequestMessage request)
    {
        foreach (var header in requestData.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.GetKey().ToString(), header.GetValue().ToString()) && requestData.Content is { })
                requestData.Content.Headers.TryAddWithoutValidation(header.GetKey().ToString(), header.GetValue().ToString());
        }
    }
}
