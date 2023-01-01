public class HttpMessageSender
{
    private readonly ConsoleWriter _writer;

    public HttpMessageSender(ConsoleWriter writer)
    {
        _writer = writer;
    }

    public async Task SendRequestAsync(HttpRequestDetails requestData, HttpBehavior behavior)
    {
        var messageHandler = new SocketsHttpHandler();
        messageHandler.MaxConnectionsPerServer = 1;
        messageHandler.AllowAutoRedirect = behavior.EnableRedirects;
        // TODO: sockets parameters

        var client = new HttpClient(messageHandler);
        var request = new HttpRequestMessage(requestData.Method, requestData.Uri);
        request.Version = requestData.Version;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Content = requestData.Content;
        client.Timeout = TimeSpan.FromSeconds(requestData.Timeout);
        SetHeaders(requestData, request);
        await SendRequest(client, request);
    }

    private static async Task SendRequest(HttpClient client, HttpRequestMessage request)
    {
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            var contentStream = await response.Content.ReadAsStringAsync();
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }
    }

    private static void SetHeaders(HttpRequestDetails requestData, HttpRequestMessage request)
    {
        foreach (var header in requestData.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.GetKey().ToString(), header.GetValue().ToString()) && requestData.Content is { })
                requestData.Content.Headers.TryAddWithoutValidation(header.GetKey().ToString(), header.GetValue().ToString());
        }
    }
}
