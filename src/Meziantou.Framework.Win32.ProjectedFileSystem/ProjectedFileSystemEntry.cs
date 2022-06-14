namespace Meziantou.Framework.Win32.ProjectedFileSystem;

public sealed class ProjectedFileSystemEntry
{
    private ProjectedFileSystemEntry(string name, bool isDirectory, int length)
    {
        IsDirectory = isDirectory;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Length = length;
    }

    public static ProjectedFileSystemEntry File(string name, int length)
    {
        return new ProjectedFileSystemEntry(name, isDirectory: false, length);
    }

    public static ProjectedFileSystemEntry Directory(string name)
    {
        return new ProjectedFileSystemEntry(name, isDirectory: true, length: 0);
    }

    public bool IsDirectory { get; }
    public string Name { get; }
    public int Length { get; }
}
