using System.Collections.Concurrent;
using CHttp.Abstractions;

namespace CHttpExtension;

internal class MemoryFileSystem : IFileSystem
{
	ConcurrentDictionary<string, byte[]> _files = new ConcurrentDictionary<string, byte[]>();

	public bool Exists(string path) => _files.ContainsKey(path);

	public byte[] GetFile(string path) => _files[path];

	public Stream Open(string path, FileMode mode, FileAccess access)
	{
		if (access == FileAccess.Read)
			return new MemoryStream(GetFile(path));
		else if (access == FileAccess.Write)
			return new TestFileStream(this, path);

		throw new NotImplementedException();
	}

	private class TestFileStream : MemoryStream
	{
		private readonly MemoryFileSystem _fileSystem;
		private readonly string _filePath;

		public TestFileStream(MemoryFileSystem fileSystem, string filePath)
		{
			_fileSystem = fileSystem;
			_filePath = filePath;
		}

		protected override void Dispose(bool disposing)
		{
			_fileSystem._files.AddOrUpdate(_filePath, ToArray(), (_, __) => ToArray());
			base.Dispose(disposing);
		}

		public override ValueTask DisposeAsync()
		{
			_fileSystem._files.AddOrUpdate(_filePath, ToArray(), (_, __) => ToArray());
			return base.DisposeAsync();
		}
	}
}
