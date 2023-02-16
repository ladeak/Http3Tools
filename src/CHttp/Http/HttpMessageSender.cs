using System.IO.Pipelines;
using System.Text;

internal sealed class HttpMessageSender
{
    private readonly IWriter _writer;

    public HttpMessageSender(IWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task SendRequestAsync(HttpRequestDetails requestData, HttpBehavior behavior)
    {
        var messageHandler = new SocketsHttpHandler();
        messageHandler.MaxConnectionsPerServer = 1;
        messageHandler.AllowAutoRedirect = behavior.EnableRedirects;
        messageHandler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions()
        {
            // TODO: sockets behavior
        };
        if (!behavior.EnableCertificateValidation)
            messageHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        var client = new HttpClient(messageHandler);
        var request = new HttpRequestMessage(requestData.Method, requestData.Uri);
        request.Version = requestData.Version;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Content = requestData.Content;
        client.Timeout = TimeSpan.FromSeconds(requestData.Timeout);
        SetHeaders(requestData, request);
        await SendRequest(client, request);
    }

    private async Task SendRequest(HttpClient client, HttpRequestMessage request)
    {
        Summary summary = new Summary(request.RequestUri?.ToString() ?? string.Empty);
        {
            try
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var charSet = response.Content.Headers.ContentType?.CharSet;
                var encoding = charSet is { } ? Encoding.GetEncoding(charSet) : Encoding.UTF8;
                await _writer.InitializeResponseAsync(response.StatusCode, response.Headers, encoding);
                await Read(response, encoding);
                summary.ReuqestCompleted();
            }
            catch (HttpRequestException requestException)
            {
                summary = summary with { Error = $"Request Error {requestException.Message}" };
            }
            catch (HttpProtocolException protocolException)
            {
                summary = summary with { Error = $"Protocol Error {protocolException.ErrorCode}" };
            }
            catch (OperationCanceledException)
            {
                summary = summary with { Error = "Request Timed Out" };
            }
            catch (Exception ex)
            {
                summary = summary with { Error = $"Generic Error {ex}" };
            }
        }

        await _writer.WriteSummaryAsync(summary);
    }

    private async Task Read(HttpResponseMessage response, Encoding encoding)
    {
        var contentStream = await response.Content.ReadAsStreamAsync();
        var transcodingStream = Encoding.CreateTranscodingStream(contentStream, encoding, Encoding.UTF8);
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
