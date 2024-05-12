namespace CHttp.Abstractions;

internal class FileSystem : IFileSystem
{
	public Stream Open(string path, FileMode mode, FileAccess access) => File.Open(path, mode, access);

    public bool Exists(string path) => File.Exists(path);
}
