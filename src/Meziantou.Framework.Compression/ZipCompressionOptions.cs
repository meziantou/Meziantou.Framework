using System.IO.Compression;

namespace Meziantou.Framework.Compression;

/// <summary>
/// Options to write a zip archive (<see cref="ZipArchive"/> in <see cref="ZipArchiveMode.Create"/> and
/// <see cref="ZipArchiveMode.Update"/> modes): the defaults of the new entries, which <see cref="ZipEntryOptions"/>
/// overrides, and the zip64 policy.
/// </summary>
public sealed class ZipCompressionOptions
{
    /// <summary>
    /// Gets or sets the compression method of the entries. The default is <see cref="ZipCompressionMethod.Deflate"/>. The
    /// methods that can be written are <see cref="ZipCompressionMethod.Stored"/>, <see cref="ZipCompressionMethod.Deflate"/>,
    /// <see cref="ZipCompressionMethod.BZip2"/>, <see cref="ZipCompressionMethod.Lzma"/>, <see cref="ZipCompressionMethod.Ppmd"/>,
    /// <see cref="ZipCompressionMethod.Xz"/> and <see cref="ZipCompressionMethod.Zstandard"/> (on .NET 11 only, whose
    /// System.IO.Compression has a Zstandard encoder); the other methods throw a <see cref="NotSupportedException"/> when an
    /// entry is created. An empty file is always stored.
    /// </summary>
    public ZipCompressionMethod CompressionMethod { get; set; } = ZipCompressionMethod.Deflate;

    /// <summary>
    /// Gets or sets the compression level of the entries. The default is <see cref="System.IO.Compression.CompressionLevel.Optimal"/>.
    /// For LZMA and XZ, the levels are the presets 0 (<see cref="System.IO.Compression.CompressionLevel.NoCompression"/>), 1
    /// (<see cref="System.IO.Compression.CompressionLevel.Fastest"/>), 6 (<see cref="System.IO.Compression.CompressionLevel.Optimal"/>)
    /// and 9 (<see cref="System.IO.Compression.CompressionLevel.SmallestSize"/>); for PPMd, the levels 1, 1, 5 and 9 of 7-Zip; for
    /// BZip2, blocks of 100 kB for the fastest levels and of 900 kB otherwise. The level does not change the method: use
    /// <see cref="ZipCompressionMethod.Stored"/> to store the content.
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// Gets or sets the password that encrypts the content of the entries (encoded as UTF-8), or <see langword="null"/> to
    /// not encrypt them. The names, the sizes and the times of the entries are not encrypted.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the encryption of the entries that have a password. The default is <see cref="ZipEncryption.Aes256"/>.
    /// <see cref="ZipEncryption.None"/> writes the entries without encryption, even with a password.
    /// <see cref="ZipEncryption.ZipCrypto"/> is weak: use it only for the tools that do not read WinZip AES.
    /// </summary>
    public ZipEncryption Encryption { get; set; } = ZipEncryption.Aes256;

    /// <summary>Gets or sets when the zip64 extensions are written. The default is <see cref="Zip64Mode.Auto"/>.</summary>
    public Zip64Mode Zip64 { get; set; }

    internal ZipCompressionOptions Clone() => (ZipCompressionOptions)MemberwiseClone();
}
