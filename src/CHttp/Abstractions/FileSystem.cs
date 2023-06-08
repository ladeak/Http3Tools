namespace CHttp.Abstractions;

internal class FileSystem : IFileSystem
{
    public Stream Open(string path, FileMode mode, FileAccess access) => File.Open(path, mode, access);
}
