using System.Net;

namespace CHttp.Http;

internal sealed class MemoryCookieContainer : ICookieContainer
{
	private CookieContainer _container = new CookieContainer();

	public CookieContainer Load() => _container;

	public Task SaveAsync() => Task.CompletedTask;
}