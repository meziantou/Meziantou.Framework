using System.Text;

namespace Meziantou.Framework.Compression;

/// <summary>Options to read an archive.</summary>
public sealed class ArchiveReaderOptions
{
    /// <summary>Gets or sets the password of an encrypted archive.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the encoding of the entry names and comments that the archive does not store as Unicode (the names of
    /// zip that are not marked as UTF-8, the OEM names of RAR 2.9). <see langword="null"/> decodes them with the code page
    /// 437 in zip, as the zip tools write them, and in RAR as UTF-8 when they are valid UTF-8, or Latin-1 otherwise.
    /// </summary>
    public Encoding? EntryNameEncoding { get; set; }

    /// <summary>
    /// Gets or sets a function that returns the volume of a multi-volume archive whose 0-based index it receives (the first
    /// volume, index 0, is the stream the archive is opened from), or <see langword="null"/> when there is no such volume.
    /// The archive disposes the streams it returns. The methods that open an archive from a path find the volumes next to
    /// the first one when this property is <see langword="null"/>.
    /// </summary>
    public Func<int, Stream?>? VolumeResolver { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes the decoders may allocate for a dictionary or a model (LZMA dictionaries, PPMd
    /// models, RAR windows). Data that need more are rejected with an <see cref="InvalidDataException"/>. The default is 2 GiB.
    /// </summary>
    public long MaxMemoryUsage { get; set; } = 2L * 1024 * 1024 * 1024;
}
