using System.IO.Compression;

namespace Meziantou.Framework.Compression.InteropTests;

/// <summary>The zip archives that <see cref="ZipArchive"/> writes, with every method and encryption, are tested and extracted by 7-Zip.</summary>
public sealed class ZipWriterInteropTests
{
    private const string Password = "secret";

    public static TheoryData<ZipCompressionMethod, ZipEncryption, bool> Cases()
    {
        var data = new TheoryData<ZipCompressionMethod, ZipEncryption, bool>();
        ZipCompressionMethod[] methods =
        [
            ZipCompressionMethod.Stored,
            ZipCompressionMethod.Deflate,
            ZipCompressionMethod.BZip2,
            ZipCompressionMethod.Lzma,
            ZipCompressionMethod.Ppmd,
            ZipCompressionMethod.Xz,
#if NET11_0_OR_GREATER
            ZipCompressionMethod.Zstandard,
#endif
        ];

        foreach (var method in methods)
        {
            foreach (var encryption in new[] { ZipEncryption.None, ZipEncryption.ZipCrypto, ZipEncryption.Aes128, ZipEncryption.Aes192, ZipEncryption.Aes256 })
            {
                data.Add(method, encryption, true);
                data.Add(method, encryption, false);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ArchivesOfTheLibraryAreExtractedBy7Zip(ZipCompressionMethod method, ZipEncryption encryption, bool seekable)
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var source = TestFiles.CreateSourceTree(folder.FullPath);
        var archivePath = folder.GetPath("library.zip");
        var options = new ZipCompressionOptions
        {
            CompressionMethod = method,
            Password = encryption is ZipEncryption.None ? null : Password,
            Encryption = encryption,
        };

        if (seekable)
        {
            ZipFile.CreateFromDirectory(source, archivePath, options);
        }
        else
        {
            using var file = File.Create(archivePath);
            using var archive = new ZipArchive(new NonSeekableStream(file), ZipArchiveMode.Create, leaveOpen: false, readerOptions: null, options);
            AddDirectory(archive, source, "");
        }

        var passwordSwitch = "-p" + (encryption is ZipEncryption.None ? "" : Password);
        await tools.RunSevenZipAsync("t", passwordSwitch, archivePath);
        var extracted = folder.GetPath("extracted");
        await tools.RunSevenZipAsync("x", "-y", passwordSwitch, "-o" + extracted, archivePath);
        Assert.Equal(TestFiles.GetTree(source), TestFiles.GetTree(extracted));
    }

    [Fact]
    public async Task UpdatedArchiveIsExtractedBy7Zip()
    {
        // An AES entry written by 7-Zip is copied without being decoded: 7-Zip still checks its authentication code
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var source = TestFiles.CreateSourceTree(folder.FullPath);
        var archivePath = folder.GetPath("7zz.zip");
        await tools.RunSevenZipAsync("a", "-tzip", "-mm=LZMA", "-mem=AES256", "-p" + Password, archivePath, Path.Combine(source, "*"));

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update, compressionOptions: new ZipCompressionOptions { Password = Password, CompressionMethod = ZipCompressionMethod.Ppmd }))
        {
            archive.GetEntry("app.exe")!.Delete();
            archive.GetEntry("readme.txt")!.Delete();
            archive.CreateEntryFromFile(Path.Combine(source, "readme.txt"), "readme.txt");
            archive.Comment = "updated";
        }

        File.Delete(Path.Combine(source, "app.exe"));
        await tools.RunSevenZipAsync("t", "-p" + Password, archivePath);
        var extracted = folder.GetPath("extracted");
        await tools.RunSevenZipAsync("x", "-y", "-p" + Password, "-o" + extracted, archivePath);
        Assert.Equal(TestFiles.GetTree(source), TestFiles.GetTree(extracted));
    }

    private static void AddDirectory(ZipArchive archive, string directory, string prefix)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var name = prefix + Path.GetFileName(path);
            if (Directory.Exists(path))
            {
                archive.CreateEntry(name + "/");
                AddDirectory(archive, path, name + "/");
            }
            else
            {
                archive.CreateEntryFromFile(path, name);
            }
        }
    }

    /// <summary>A write-only stream that is not seekable.</summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
