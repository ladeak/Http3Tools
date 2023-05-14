using System.IO;
using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using CHttp.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Http3Parts.Tests;

[Collection(nameof(NonParallel))]
public class H3ClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [QuicSupportedFact]
    public async Task Test_VanillaRequest()
    {
        using var app = HttpServer.CreateHostBuilder(TestResponseAsync, HttpProtocols.Http3, port: 5001);
        await app.StartAsync();
        await using (var client = new H3Client())
            await client.TestAsync(new Uri("https://localhost:5001"));
        await app.StopAsync();
    }

    [QuicSupportedTheory]
    [InlineData("/api/resource")]
    [InlineData("/")]
    public async Task Test_CustomPath(string path)
    {
        using var app = HttpServer.CreateHostBuilder(TestResponseAsync, HttpProtocols.Http3, port: 5001, path: path);
        await app.StartAsync();

        UriBuilder builder = new UriBuilder("https://localhost:5001");
        builder.Path = path;
        await using (var client = new H3Client())
            await client.TestAsync(builder.Uri);

        await app.StopAsync();
    }

    [QuicSupportedFact]
    public async Task OpenAsync_Sets_ConnectionContext()
    {
        using var app = HttpServer.CreateHostBuilder(TestResponseAsync, HttpProtocols.Http3, port: 5001);
        await app.StartAsync();

        await using (var client = new H3Client())
        {
            await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 5001));
            Assert.NotNull(client.ConnectionContext);
        }

        await app.StopAsync();
    }

    [QuicSupportedFact]
    public async Task SendSettingsAsync_SendsSettingsBytes()
    {
        await using QuicListener listener = await CreateListener();
        var connectionTask = listener.AcceptConnectionAsync();

        var client = new H3Client();
        await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 5001));

        var connection = await connectionTask.AsTask().WaitAsync(Timeout);
        var serverStreamTask = connection.AcceptInboundStreamAsync();

        await client.SendSettingsAsync();

        var expected = new byte[] { 0, 4, 3, 6, 68, 0 };
        var dataRead = new byte[expected.Length];
        var serverStream = await serverStreamTask;
        await serverStream.ReadAtLeastAsync(dataRead, dataRead.Length, true, CancellationToken.None);
        Assert.True(dataRead.SequenceEqual(expected));
    }

    [QuicSupportedFact]
    public async Task CustomSettingsAsync_SendSettingsAsync_SendsBytes()
    {
        await using QuicListener listener = await CreateListener();
        var connectionTask = listener.AcceptConnectionAsync();

        var client = new H3Client();
        await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 5001));

        var connection = await connectionTask.AsTask().WaitAsync(Timeout);
        var serverStreamTask = connection.AcceptInboundStreamAsync();

        await client.SendSettingsAsync(new SettingParameter(0x6, 1023));

        var expected = new byte[] { 0, 4, 3, 6, 67, 255 };
        var dataRead = new byte[expected.Length];
        var serverStream = await serverStreamTask;
        await serverStream.ReadAtLeastAsync(dataRead, dataRead.Length, true, CancellationToken.None);
        Assert.True(dataRead.SequenceEqual(expected));
    }

    private async Task<QuicListener> CreateListener()
    {
        QuicListener listener = await QuicListener.ListenAsync(new QuicListenerOptions()
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Any, 5001),
            ApplicationProtocols = new List<SslApplicationProtocol>()
            {
                SslApplicationProtocol.Http3
            },
            ConnectionOptionsCallback = async (connection, sslHello, cancellationToken) =>
            {
                return new QuicServerConnectionOptions()
                {
                    MaxInboundBidirectionalStreams = 0,
                    MaxInboundUnidirectionalStreams = 1,
                    DefaultStreamErrorCode = 0x10c, //RequestCancelled,
                    DefaultCloseErrorCode = 0x100, //NoError,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions()
                    {
                        ApplicationProtocols = new List<SslApplicationProtocol>() { SslApplicationProtocol.Http3 },
                        ServerCertificate = new X509Certificate2("testCert.pfx", "testPassword"),
                    }
                };
            }
        });
        return listener;
    }

    private async Task TestResponseAsync(HttpContext context)
    {
        await context.Response.WriteAsync("hello world");
    }
}
