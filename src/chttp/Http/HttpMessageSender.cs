using System.Buffers;
using System.Text;

public class HttpMessageSender
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
        try
        {
            var summary = new Summary();
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var charSet = response.Content.Headers.ContentType?.CharSet;
            var encoding = charSet is { } ? Encoding.GetEncoding(charSet) : Encoding.UTF8;
            var contentStream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(contentStream, encoding);
            var buffer = ArrayPool<char>.Shared.Rent(8192);
            int count = int.MaxValue;
            while (count > 0)
            {
                count = await reader.ReadAsync(buffer);
                _writer.Write(buffer.AsSpan(0, count));
            }
            ArrayPool<char>.Shared.Return(buffer);
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }
        catch (Exception ex)
        {

        }
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
