namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Represents a file or directory entry in a virtual file system.</summary>
public sealed class ProjectedFileSystemEntry
{
    private ProjectedFileSystemEntry(string name, bool isDirectory, int length)
    {
        IsDirectory = isDirectory;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Length = length;
    }

    /// <summary>Creates a file entry.</summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="length">The size of the file in bytes.</param>
    /// <returns>A <see cref="ProjectedFileSystemEntry"/> representing a file.</returns>
    public static ProjectedFileSystemEntry File(string name, int length)
    {
        return new ProjectedFileSystemEntry(name, isDirectory: false, length);
    }

    /// <summary>Creates a directory entry.</summary>
    /// <param name="name">The name of the directory.</param>
    /// <returns>A <see cref="ProjectedFileSystemEntry"/> representing a directory.</returns>
    public static ProjectedFileSystemEntry Directory(string name)
    {
        return new ProjectedFileSystemEntry(name, isDirectory: true, length: 0);
    }

    /// <summary>Gets a value indicating whether this entry represents a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Gets the name of the file or directory.</summary>
    public string Name { get; }

    /// <summary>Gets the size of the file in bytes. Always 0 for directories.</summary>
    public int Length { get; }
}
