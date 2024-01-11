using System.Net;

namespace CHttp.Http;

internal interface ICookieContainer
{
	CookieContainer Load();

	Task SaveAsync();
}