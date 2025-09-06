﻿using System.Net;
using System.Net.Quic;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace CHttpServer;

public class CHttpServerImpl : IServer
{
    private readonly CHttpServerOptions _options;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly FeatureCollection _features;
    private readonly Http2CHttpServer _http2Server;
    private readonly Http3CHttpServer? _http3Server;

    public CHttpServerImpl(IOptions<CHttpServerOptions> options)
    {
        _options = options.Value;
        _cancellationTokenSource = new CancellationTokenSource();
        _features = new FeatureCollection();
        var serverAddresses = new ServerAddressesFeature();
        Features.Set<IServerAddressesFeature>(serverAddresses);
        Features.Set<IMemoryPoolFeature>(new CHttpMemoryPool());
        _http2Server = new Http2CHttpServer(options, _features, _cancellationTokenSource.Token);
        if (_options.UseHttp3)
            _http3Server = new Http3CHttpServer(options, _features, _cancellationTokenSource.Token);
    }

    public IFeatureCollection Features => _features;

    public void Dispose()
    {
    }

    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
    {
        var addresses = Features.Get<IServerAddressesFeature>()?.Addresses;
        var httpsAddress = addresses?.FirstOrDefault(x => x.StartsWith("https://"));

        Uri? uri = null;
        if (httpsAddress != null && !Uri.TryCreate(httpsAddress, UriKind.Absolute, out uri))
            throw new ArgumentNullException("Valid https address required");
        var ip = _options.Host;
        if (ip == null)
        {
            if (!IPAddress.TryParse(uri?.Host, out ip))
            {
                if (uri?.Host == "localhost")
                    ip = IPAddress.Loopback;
                else
                    ip = IPAddress.Any;
            }
        }
        var endpoint = new IPEndPoint(ip, uri?.Port ?? _options.Port ?? 5001);
        cancellationToken.Register(() => _cancellationTokenSource.Cancel());
        if (addresses != null && !addresses.IsReadOnly && addresses.Count == 0)
            addresses.Add($"https://{endpoint.Address}:{endpoint.Port}");

        var h2Listener = _http2Server.StartAsync(endpoint, application, _cancellationTokenSource.Token);
        var h3Listener = Task.CompletedTask;
        if (_http3Server != null && QuicListener.IsSupported)
            h3Listener = _http3Server.StartAsync(endpoint, application, _cancellationTokenSource.Token);

        return Task.WhenAll(h2Listener, h3Listener);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }
}
