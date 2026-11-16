using System.IO.Compression;
using Meziantou.Framework.Compression;

namespace Meziantou.Framework.FileConversionTool.Archives;

internal sealed class SevenZipArchiveWriter : ArchiveWriter
{
    private readonly SevenZipArchive _archive;

    public SevenZipArchiveWriter(Stream stream, ConversionSettings settings, bool storeByDefault)
    {
        var store = (storeByDefault && settings.ArchiveCompressionLevel is null && settings.SevenZipMethod is null) || settings.ArchiveCompressionLevel is CompressionLevel.NoCompression;
        var options = new SevenZipCompressionOptions
        {
            Method = store ? SevenZipCompressionMethod.Copy : settings.SevenZipMethod ?? SevenZipCompressionMethod.Lzma2,
            Solid = settings.SevenZipSolid,
        };

        if (settings.ArchiveCompressionLevel is { } level)
        {
            options.Level = level switch
            {
                CompressionLevel.Fastest => 1,
                CompressionLevel.SmallestSize => 9,
                CompressionLevel.NoCompression => 0,
                _ => 5,
            };
        }

        if (settings.SevenZipPassword is { } password)
        {
            options.Password = password;
            options.EncryptHeaders = settings.SevenZipEncryptHeaders;
        }

        _archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options);
    }

    public override void AddDirectory(string name, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode)
    {
        var entry = _archive.CreateEntry(name + "/");
        entry.SetLastWriteTime(lastWriteTime);
        entry.SetUnixFileMode(unixFileMode);
    }

    public override void AddFile(string name, Stream content, DateTimeOffset? lastWriteTime, UnixFileMode? unixFileMode)
    {
        var entry = _archive.CreateEntry(name);
        entry.SetLastWriteTime(lastWriteTime);
        entry.SetUnixFileMode(unixFileMode);
        using var stream = entry.Open();
        content.CopyTo(stream);
    }

    // The writer of the library stores a link only from a link of the file system
    public override bool AddSymbolicLink(string name, string target, DateTimeOffset? lastWriteTime) => false;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _archive.Dispose();
        }
    }
}
