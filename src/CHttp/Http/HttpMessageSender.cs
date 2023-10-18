using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Http;

namespace CHttp;

internal sealed class HttpMessageSender
{
	private readonly IWriter _writer;
	private readonly HttpClient _client;
	private readonly HttpBehavior _behavior;

	public HttpMessageSender(IWriter writer, ICookieContainer cookieContainer, HttpBehavior behavior)
	{
		_writer = writer ?? throw new ArgumentNullException(nameof(writer));
		_behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
		var messageHandler = new SocketsHttpHandler();
		messageHandler.MaxConnectionsPerServer = 1;
		messageHandler.AllowAutoRedirect = behavior.EnableRedirects;
		messageHandler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions()
		{
			// TODO: sockets behavior
			CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Offline,
		};

		if (!behavior.EnableCertificateValidation)
			messageHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

		messageHandler.UseCookies = true;
		messageHandler.CookieContainer = cookieContainer.Load();

		_client = new HttpClient(messageHandler);
		_client.Timeout = TimeSpan.FromSeconds(behavior.Timeout);
	}

	public HttpMessageSender(IWriter writer, HttpClient client)
	{
		_writer = writer ?? throw new ArgumentNullException(nameof(writer));
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_behavior = new(false, false, 0, ToUtf8: false, CookieContainer: string.Empty);
	}

	public async Task SendRequestAsync(HttpRequestDetails requestData)
	{
		var request = new HttpRequestMessage(requestData.Method, requestData.Uri);
		request.Version = requestData.Version;
		request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
		if (requestData.Content is MemoryArrayContent source)
			request.Content = new MemoryArrayContent(source);
		else
			request.Content = requestData.Content;
		SetHeaders(requestData, request);
		await SendRequestAsync(_client, request);
	}

	public async Task SendRequestAsync(HttpRequestMessage request) => await SendRequestAsync(_client, request);

	private async Task SendRequestAsync(HttpClient client, HttpRequestMessage request)
	{
		Summary summary = new Summary(request.RequestUri?.ToString() ?? string.Empty);
		HttpResponseHeaders? trailers = null;
		{
			try
			{
				var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
				var charSet = response.Content.Headers.ContentType?.CharSet;
				var encoding = charSet is { } ? Encoding.GetEncoding(charSet) : Encoding.UTF8;
				await _writer.InitializeResponseAsync(new HttpResponseInitials(response.StatusCode, response.Headers, response.Content.Headers, response.Version, encoding));
				await ProcessResponseAsync(response, encoding);
				summary.RequestCompleted(response.StatusCode);
				trailers = response.TrailingHeaders;
			}
			catch (HttpRequestException requestException)
			{
				summary = summary with { Error = $"Request Error {requestException}", ErrorCode = ErrorType.HttpRequestException };
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
			var headerKey = header.GetKey().ToString();
			var headerValue = header.GetValue().ToString();
			if (!request.Headers.TryAddWithoutValidation(headerKey, headerValue) && request.Content is { })
			{
				if (string.Equals(headerKey, "Content-Type", StringComparison.OrdinalIgnoreCase))
				{
					request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(headerValue);
				}
				else
				{
					// Removing the header when overriden by the user.
					request.Content.Headers.Remove(headerKey);
					request.Content.Headers.TryAddWithoutValidation(headerKey, headerValue);
				}
			}
		}
	}
}
