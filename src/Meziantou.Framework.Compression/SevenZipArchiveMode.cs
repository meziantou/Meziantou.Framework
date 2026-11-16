namespace Meziantou.Framework.Compression;

/// <summary>The mode of a <see cref="SevenZipArchive"/>.</summary>
public enum SevenZipArchiveMode
{
    /// <summary>The archive is read: its entries can be opened for reading.</summary>
    Read,

    /// <summary>The archive is created: entries can be added, and the archive is written when it is disposed. The stream must be seekable.</summary>
    Create,

    /// <summary>
    /// The archive is read and modified: entries can be read, added and deleted, and the archive is rewritten when it is
    /// disposed. The stream must be readable, writable and seekable.
    /// </summary>
    Update,
}
