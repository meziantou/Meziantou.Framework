namespace Meziantou.Framework.FileConversionTool.Archives;

/// <summary>Writes the entries of an archive output: zip, 7z or tar, compressed or not.</summary>
internal abstract class ArchiveWriter : IDisposable
{
    /// <summary>Creates the writer of an archive.</summary>
    /// <param name="stream">The output file.</param>
    /// <param name="archiveType">The container to write.</param>
    /// <param name="settings">The options of the command line.</param>
    /// <param name="storeByDefault">Store the entries without compression unless the command line sets a compression: the images of a comic book archive are already compressed.</param>
    public static ArchiveWriter Create(Stream stream, ArchiveType archiveType, ConversionSettings settings, bool storeByDefault) => archiveType switch
    {
        ArchiveType.Zip => new ZipArchiveWriter(stream, settings, storeByDefault),
        ArchiveType.SevenZip => new SevenZipArchiveWriter(stream, settings, storeByDefault),
        ArchiveType.Tar or ArchiveType.TarGzip or ArchiveType.TarBzip2 or ArchiveType.TarXz or ArchiveType.TarLzma or ArchiveType.TarLzip => new TarArchiveWriter(stream, archiveType, settings),
        _ => throw new ArgumentOutOfRangeException(nameof(archiveType), archiveType, "The archive format cannot be written"),
    };

    /// <summary>Adds a directory. The name does not end with <c>/</c>.</summary>
    public abstract void AddDirectory(string name, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode);

    /// <summary>Adds a file with its content.</summary>
    public abstract void AddFile(string name, Stream content, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode);

    /// <summary>Adds a symbolic link, and returns false when the format cannot store one.</summary>
    public abstract bool AddSymbolicLink(string name, string target, DateTimeOffset? lastWriteTime);

    /// <summary>Adds a hard link to a file added before, and returns false when the format cannot store one.</summary>
    public virtual bool AddHardLink(string name, string target, DateTimeOffset? lastWriteTime) => false;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);
}
