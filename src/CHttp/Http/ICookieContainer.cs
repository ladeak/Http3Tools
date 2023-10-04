using System.Net;

internal interface ICookieContainer
{
	Task<CookieContainer> GetContainerAsync();

	Task PersistContainerAsync();
}