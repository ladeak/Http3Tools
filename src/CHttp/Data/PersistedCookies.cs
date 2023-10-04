namespace CHttp.Data;

internal record class PersistedCookies(IReadOnlyCollection<PersistedCookie> Cookies)
{
	public static PersistedCookies Default { get; } = new PersistedCookies(Array.Empty<PersistedCookie>());
}
