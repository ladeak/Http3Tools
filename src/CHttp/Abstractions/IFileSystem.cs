namespace CHttp.Abstractions;

internal interface IFileSystem
{
    Stream Open(string path, FileMode mode, FileAccess access);
}
