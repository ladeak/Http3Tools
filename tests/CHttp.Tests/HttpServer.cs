using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CHttp.Tests;

public static class HttpServer
{
	public static WebApplication CreateHostBuilder(RequestDelegate? requestDelegate = null,
		HttpProtocols? protocol = null,
		Action<KestrelServerOptions>? configureKestrel = null,
		Action<IServiceCollection>? configureServices = null,
		Action<WebApplication>? configureApp = null,
		int port = 5011,
		string path = "/")
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseKestrel(kestrel =>
		{
			kestrel.ListenAnyIP(port, options =>
			{
				options.UseHttps(new X509Certificate2("testCert.pfx", "testPassword"));
				options.Protocols = protocol ?? HttpProtocols.Http3;
			});
			configureKestrel?.Invoke(kestrel);
		});

		configureServices?.Invoke(builder.Services);
		var app = builder.Build();

		if (requestDelegate != null)
			app.Map(path, requestDelegate);
		else if (configureApp != null)
			configureApp.Invoke(app);
		return app;
	}
}