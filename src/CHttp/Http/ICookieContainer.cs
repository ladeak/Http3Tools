using System.Net;

internal interface ICookieContainer
{
	CookieContainer Load();

	Task SaveAsync();
}