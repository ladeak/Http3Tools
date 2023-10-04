using System.Net;

namespace CHttp.Data;

internal record class PersistedCookie(string Key, string Value, string Path, string Domain, DateTime Expires, bool HttpOnly, bool Secure)
{
	public static implicit operator Cookie(PersistedCookie source)
	{
		return new Cookie(source.Key, source.Value, source.Path, source.Domain)
		{
			Domain = source.Domain,
			Secure = source.Secure,
			HttpOnly = source.HttpOnly,
			Expires = source.Expires,
		};
	}

	public static implicit operator PersistedCookie(Cookie source)
	{
		return new PersistedCookie(source.Name, source.Value, source.Path, source.Domain, source.Expires, source.HttpOnly, source.Secure);
	}
}
