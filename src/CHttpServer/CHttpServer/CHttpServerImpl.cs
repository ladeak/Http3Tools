using System.Net;
using System.Net.Quic;
using CHttpServer.Http3;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace CHttpServer;

public class CHttpServerImpl : IServer
{
    private readonly CHttpServerOptions _options;
    private readonly FeatureCollection _features;
    private readonly Http2CHttpServer _http2Server;
    private readonly Http3CHttpServer? _http3Server;

    public CHttpServerImpl(IOptions<CHttpServerOptions> options)
    {
        _options = options.Value;
        _features = new FeatureCollection();
        var serverAddresses = new ServerAddressesFeature();
        Features.Set<IServerAddressesFeature>(serverAddresses);
        Features.Set<IMemoryPoolFeature>(new CHttpMemoryPool());
        _http2Server = new Http2CHttpServer(options, _features);
        if (_options.UseHttp3)
            _http3Server = new Http3CHttpServer(options, _features);
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
        if (addresses != null && !addresses.IsReadOnly && addresses.Count == 0)
            addresses.Add($"https://{endpoint.Address}:{endpoint.Port}");

        var h2Listener = _http2Server.StartAsync(endpoint, application, cancellationToken);
        var h3Listener = Task.CompletedTask;
        if (_http3Server != null && QuicListener.IsSupported)
            h3Listener = _http3Server.StartAsync(endpoint, application, cancellationToken);

        return Task.WhenAll(h2Listener, h3Listener);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var h2Stopping = _http2Server.StopAsync(cancellationToken);
        if (_http3Server != null && QuicListener.IsSupported)
            return Task.WhenAll(h2Stopping, _http3Server.StopAsync(cancellationToken));
        return h2Stopping;
    }
}
