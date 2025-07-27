using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CHttpServer;

public class CHttpServerOptions
{
    internal const uint InitialStreamFlowControlSize = 65_535;

    public int? Port { get; set; }

    public IPAddress? Host { get; set; }

    public uint ServerConnectionFlowControlSize { get; set; } = InitialStreamFlowControlSize * 10;

    public uint ServerStreamFlowControlSize { get; set; } = InitialStreamFlowControlSize;

    public int MaxRequestHeaderLength { get; set; } = 65_535;

    public bool UsePriority { get; set; }

    public X509Certificate2? Certificate { get; set; }

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    internal X509Certificate2? GetCertificate()
    {
        if (Certificate != null)
            return Certificate;
        if (!string.IsNullOrEmpty(CertificatePath) && !string.IsNullOrEmpty(CertificatePassword))
            return X509CertificateLoader.LoadPkcs12FromFile(CertificatePath, CertificatePassword);
        return GetDevelopmentCertificateFromStore();
    }

    private static X509Certificate2? GetDevelopmentCertificateFromStore()
    {
        const string AspNetHttpsOid = "1.3.6.1.4.1.311.84.1.1";
        const string AspNetHttpsOidFriendlyName = "ASP.NET Core HTTPS development certificate";
        var now = DateTimeOffset.Now;
        try
        {
            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser, OpenFlags.ReadOnly);
            var certs = store.Certificates
                .OfType<X509Certificate2>()
                .Where(x => string.Equals(x.FriendlyName, AspNetHttpsOidFriendlyName, StringComparison.OrdinalIgnoreCase)
                  && x.Extensions.Any(ext => ext.Oid != null
                  && string.Equals(ext.Oid.Value, AspNetHttpsOid, StringComparison.OrdinalIgnoreCase)
                  && x.NotBefore <= now && x.NotAfter >= now))
                .ToList();

            return certs.Count > 0 ? certs[0] : null;
        }
        catch
        {
            return null;
        }
    }
}

public static class HostExtensions
{
    public static WebApplicationBuilder UseCHttpServer(this WebApplicationBuilder builder, Action<CHttpServerOptions>? configure = null)
    {
        builder.Services.TryAddSingleton<IConnectionListenerFactory, SocketTransportFactory>();
        builder.Services.Configure<CHttpServerOptions>(builder.Configuration.GetSection("CHttpServer"));
        if (configure != null)
            builder.Services.PostConfigure<CHttpServerOptions>(configure);
        builder.Services.AddSingleton<IServer, CHttpServerImpl>();
        return builder;
    }
}
