using System.IO.Compression;
using Meziantou.Framework.Compression;
using ZipArchive = Meziantou.Framework.Compression.ZipArchive;
using ZipArchiveMode = System.IO.Compression.ZipArchiveMode;
using ZipCompressionMethod = Meziantou.Framework.Compression.ZipCompressionMethod;

namespace Meziantou.Framework.FileConversionTool.Archives;

internal sealed class ZipArchiveWriter : ArchiveWriter
{
    private readonly ZipArchive _archive;

    public ZipArchiveWriter(Stream stream, ConversionSettings settings, bool storeByDefault)
    {
        var store = storeByDefault && settings.ArchiveCompressionLevel is null && settings.ZipMethod is null;
        var options = new ZipCompressionOptions
        {
            CompressionLevel = settings.ArchiveCompressionLevel ?? CompressionLevel.Optimal,
            CompressionMethod = store ? ZipCompressionMethod.Stored : (settings.ZipMethod ?? ZipMethod.Deflate) switch
            {
                ZipMethod.Stored => ZipCompressionMethod.Stored,
                ZipMethod.BZip2 => ZipCompressionMethod.BZip2,
                ZipMethod.Lzma => ZipCompressionMethod.Lzma,
                ZipMethod.Ppmd => ZipCompressionMethod.Ppmd,
                ZipMethod.Xz => ZipCompressionMethod.Xz,
                ZipMethod.Zstandard => ZipCompressionMethod.Zstandard,
                _ => ZipCompressionMethod.Deflate,
            },
        };

        if (settings.ZipPassword is { } password)
        {
            options.Password = password;
            options.Encryption = settings.ZipEncryption switch
            {
                ZipEncryptionMethod.Aes128 => ZipEncryption.Aes128,
                ZipEncryptionMethod.Aes192 => ZipEncryption.Aes192,
                ZipEncryptionMethod.ZipCrypto => ZipEncryption.ZipCrypto,
                _ => ZipEncryption.Aes256,
            };
        }

        _archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options);
    }

    public override void AddDirectory(string name, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode)
    {
        _archive.CreateEntry(name + "/", new ZipEntryOptions { LastWriteTime = lastWriteTime, UnixFileMode = unixFileMode });
    }

    public override void AddFile(string name, Stream content, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode)
    {
        var entry = _archive.CreateEntry(name, new ZipEntryOptions { LastWriteTime = lastWriteTime, UnixFileMode = unixFileMode });
        using var stream = entry.Open();
        content.CopyTo(stream);
    }

    public override bool AddSymbolicLink(string name, string target, DateTimeOffset? lastWriteTime)
    {
        _archive.CreateSymbolicLinkEntry(name, target, new ZipEntryOptions { LastWriteTime = lastWriteTime });
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _archive.Dispose();
        }
    }
}
