﻿using System.Net;
using System.Text.Json;
using CHttp.Abstractions;
using CHttp.Data;

namespace CHttp.Http;

internal sealed class PersistentCookieContainer : ICookieContainer
{
	private CookieContainer _container = new CookieContainer();

	public PersistentCookieContainer(IFileSystem fileSystem, string name)
	{
		FileSystem = fileSystem;
		Name = name;
	}

	private IFileSystem FileSystem { get; }

	private string Name { get; }

	public CookieContainer Load()
	{
		if (string.IsNullOrWhiteSpace(Name) || !FileSystem.Exists(Name))
			return _container;

		using var stream = FileSystem.Open(Name, FileMode.Open, FileAccess.Read);
		var cookieCollection = JsonSerializer.Deserialize(stream, KnownJsonType.Default.PersistedCookies) ?? PersistedCookies.Default;

		if (cookieCollection.Cookies.Count == 0)
			return _container;

		foreach (var cookie in cookieCollection.Cookies)
			_container.Add((Cookie)cookie);

		return _container;
	}

	public async Task SaveAsync()
	{
		if (string.IsNullOrWhiteSpace(Name))
			return;

		var cookies = _container.GetAllCookies().Select(x => (PersistedCookie)x).ToArray();
		using var stream = FileSystem.Open(Name, FileMode.Create, FileAccess.Write);
		await JsonSerializer.SerializeAsync(stream, new PersistedCookies(cookies), KnownJsonType.Default.PersistedCookies);
		await stream.FlushAsync();
		return;
	}
}
