using System.Collections.Concurrent;
using CHttp.Abstractions;

namespace CHttp.Tests;

internal class TestFileSystem : IFileSystem
{
    ConcurrentDictionary<string, byte[]> _files = new ConcurrentDictionary<string, byte[]>();

    public byte[] GetFile(string path) => _files[path];

    public Stream Open(string path, FileMode mode, FileAccess access) => new TestFileStream(this, path);

    private class TestFileStream : MemoryStream 
    {
        private readonly TestFileSystem _fileSystem;
        private readonly string _filePath;

        public TestFileStream(TestFileSystem fileSystem, string filePath)
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
