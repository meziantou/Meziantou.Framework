namespace Meziantou.Framework.Compression;

/// <summary>When a <see cref="ZipArchive"/> writes the zip64 extensions (APPNOTE 4.3.14, 4.5.3).</summary>
public enum Zip64Mode
{
    /// <summary>
    /// The zip64 fields are written only when a size, an offset or the number of entries does not fit in the classic
    /// fields. On a seekable stream, an entry that turns out larger than 4 GiB gets a zip64 data descriptor after its
    /// content. On a stream that is not seekable, the sizes are not known when the local header is written: an entry
    /// larger than 4 GiB gets a zip64 data descriptor, which the readers that use the central directory read (7-Zip,
    /// Info-ZIP, <see cref="System.IO.Compression.ZipArchive"/>, <see cref="ZipArchive"/>), but which the
    /// specification announces with a zip64 extra field in the local header: use <see cref="Always"/> when an entry of more
    /// than 4 GiB written to a stream that is not seekable must be read forward only by other tools.
    /// </summary>
    Auto,

    /// <summary>
    /// Every entry has a zip64 extra field in its local header and in the central directory, its data descriptor has
    /// 8-byte sizes, and the archive ends with the zip64 end of central directory record. Some old tools do not read zip64.
    /// </summary>
    Always,
}
