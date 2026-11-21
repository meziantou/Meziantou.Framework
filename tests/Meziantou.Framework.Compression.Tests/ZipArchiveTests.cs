using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Zip;
using System.Security.Cryptography;

namespace Meziantou.Framework.Compression.Tests;

public sealed class ZipArchiveTests
{
    [Theory]
    [MemberData(nameof(ZipTestData.Fixtures), MemberType = typeof(ZipTestData))]
    public void ReadEntries(string fileName, string? password, string[] expectedFiles)
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead(fileName), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = password }, compressionOptions: null);
        Assert.Equal(ArchiveFormat.Zip, archive.Format);
        var files = new List<string>();
        foreach (var entry in archive.Entries)
        {
            Assert.Same(entry, archive.GetEntry(entry.FullName));
            if (entry.IsDirectory)
                continue;

            files.Add(entry.FullName);
            using var stream = entry.Open();
            var content = ZipTestData.ReadAll(stream);
            Assert.Equal(ZipTestData.GetExpectedHash(entry.FullName), ZipTestData.Hash(content));
            Assert.Equal(entry.Length, content.Length);
        }

        Assert.Equal(expectedFiles.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ZipTestData.Fixtures), MemberType = typeof(ZipTestData))]
    public async Task ReadEntriesAsync(string fileName, string? password, string[] expectedFiles)
    {
        await using var archive = new ZipArchive(ZipTestData.OpenRead(fileName), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = password }, compressionOptions: null);
        var files = new List<string>();
        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            files.Add(entry.FullName);
            await using var stream = await entry.OpenAsync(TestContext.Current.CancellationToken);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
            Assert.Equal(ZipTestData.GetExpectedHash(entry.FullName), ZipTestData.Hash(copy.ToArray()));
        }

        Assert.Equal(expectedFiles.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ZipTestData.BclFixtures), MemberType = typeof(ZipTestData))]
    public void MatchesZipArchive(string fileName, string? password)
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead(fileName), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = password }, compressionOptions: null);
        using var expected = new System.IO.Compression.ZipArchive(ZipTestData.OpenRead(fileName), ZipArchiveMode.Read);
        Assert.HasCount(expected.Entries.Count, archive.Entries);
        for (var i = 0; i < expected.Entries.Count; i++)
        {
            var expectedEntry = expected.Entries[i];
            var entry = archive.Entries[i];
            Assert.Equal(expectedEntry.FullName, entry.FullName);
            Assert.Equal(expectedEntry.Length, entry.Length);
            Assert.Equal(expectedEntry.CompressedLength, entry.CompressedLength);
            Assert.Equal(expectedEntry.Comment, entry.Comment);
            Assert.Equal(expectedEntry.ExternalAttributes, entry.ExternalAttributes);
            Assert.Equal(expectedEntry.IsEncrypted, entry.IsEncrypted);
            if (entry.Crc32 is not null)
            {
                Assert.Equal(expectedEntry.Crc32, entry.Crc32);
            }

            // ZipArchive reads the MS-DOS time only
            Assert.Equal(expectedEntry.LastWriteTime, ZipEntryInfo.DecodeDosTime(entry.Info.DosDate, entry.Info.DosTime));
            if (entry.IsDirectory)
                continue;

#if NET11_0_OR_GREATER
            using var expectedStream = password is null ? expectedEntry.Open() : expectedEntry.Open(password);
#else
            using var expectedStream = expectedEntry.Open();
#endif
            using var stream = entry.Open();
            Assert.Equal(ZipTestData.ReadAll(expectedStream), ZipTestData.ReadAll(stream));
        }
    }

    [Theory]
    [InlineData("SHRUNK.ZIP", ZipCompressionMethod.Shrink)]
    [InlineData("imploding-4Kdict-2trees.zip", ZipCompressionMethod.Implode)]
    [InlineData("imploding-8Kdict-3trees.zip", ZipCompressionMethod.Implode)]
    [InlineData("deflate64-text.zip", ZipCompressionMethod.Deflate64)]
    [InlineData("deflate64-far.zip", ZipCompressionMethod.Deflate64)]
    public void LegacyMethods(string fileName, ZipCompressionMethod method)
    {
        // The streams check the length and the CRC-32 of each entry; the forward-only reader returns the same contents
        var data = File.ReadAllBytes(ZipTestData.GetLegacyPath(fileName));
        using var archive = new ZipArchive(new MemoryStream(data));
        using var reader = ZipArchive.CreateForwardReader(ZipTestData.CreateForwardOnlyStream(data, 7), options: null, leaveOpen: false);
        Assert.NotEmpty(archive.Entries);
        foreach (var entry in archive.Entries)
        {
            Assert.True(reader.MoveToNextEntry());
            Assert.Equal(entry.FullName, reader.Entry!.FullName);
            if (entry.IsDirectory)
                continue;

            Assert.Equal(method, entry.CompressionMethod);
            var content = ZipTestData.ReadAll(entry.Open());
            Assert.Equal(entry.Length, content.Length);
            Assert.Equal(content, ZipTestData.ReadAll(reader.OpenEntryStream()));
        }

        Assert.False(reader.MoveToNextEntry());
    }

    [Theory]
    [InlineData("deflate64-text.zip")]
    [InlineData("deflate64-far.zip")]
    public void Deflate64_MatchesZipArchive(string fileName)
    {
        using var archive = new ZipArchive(File.OpenRead(ZipTestData.GetLegacyPath(fileName)));
        using var expected = new System.IO.Compression.ZipArchive(File.OpenRead(ZipTestData.GetLegacyPath(fileName)), ZipArchiveMode.Read);
        foreach (var expectedEntry in expected.Entries)
        {
            Assert.Equal(ZipTestData.ReadAll(expectedEntry.Open()), ZipTestData.ReadAll(archive.GetEntry(expectedEntry.FullName)!.Open()));
        }
    }

    [Fact]
    public void ZipArchive_Read()
    {
        #region ZipArchive_Read
        // A zip archive written by System.IO.Compression.ZipArchive
        using var stream = new MemoryStream();
        using (var writer = new System.IO.Compression.ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entryStream = writer.CreateEntry("docs/readme.txt").Open();
            entryStream.Write("Hello, world!"u8);
        }

        stream.Position = 0;
        using var archive = new ZipArchive(stream);
        Assert.HasCount(1, archive.Entries);

        var entry = archive.GetEntry("docs/readme.txt")!;
        Assert.Equal(ZipCompressionMethod.Deflate, entry.CompressionMethod);
        Assert.Equal(13L, entry.Length);

        // The stream checks the length and the CRC-32 of the content
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("Hello, world!", reader.ReadToEnd());
        #endregion
    }

    [Fact]
    public void ZipArchive_ReadPassword()
    {
        #region ZipArchive_ReadPassword
        // An archive created by 7-Zip: 7zz a -tzip -mem=AES256 -ppassword aes256.zip ...
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Zip", "aes256.zip");
        using var archive = ZipFile.OpenRead(path, new ArchiveReaderOptions { Password = "password" });

        var entry = archive.GetEntry("hello.txt")!;
        Assert.True(entry.IsEncrypted);
        Assert.Equal(ZipEncryption.Aes256, entry.EncryptionMethod);

        using var reader = new StreamReader(entry.Open());
        Assert.Equal("Hello, world!\n", reader.ReadToEnd());
        #endregion
    }

    [Theory]
    [InlineData("zipcrypto.zip", ZipEncryption.ZipCrypto)]
    [InlineData("zipcrypto-infozip.zip", ZipEncryption.ZipCrypto)]
    [InlineData("zipcrypto-bsdtar.zip", ZipEncryption.ZipCrypto)]
    [InlineData("aes128.zip", ZipEncryption.Aes128)]
    [InlineData("aes192.zip", ZipEncryption.Aes192)]
    [InlineData("aes256.zip", ZipEncryption.Aes256)]
    [InlineData("aes-bsdtar.zip", ZipEncryption.Aes256)]
    public void Password(string fileName, ZipEncryption encryption)
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead(fileName));
        var entry = archive.GetEntry("lorem.txt")!;
        Assert.True(entry.IsEncrypted);
        Assert.Equal(encryption, entry.EncryptionMethod);
        Assert.Throws<ArchivePasswordException>(() => entry.Open());

        using var wrong = new ZipArchive(ZipTestData.OpenRead(fileName), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = "wrong" }, compressionOptions: null);
        Assert.Throws<ArchivePasswordException>(() => ZipTestData.ReadAll(wrong.GetEntry("lorem.txt")!.Open()));
        Assert.Throws<ArchivePasswordException>(() => ZipTestData.ReadAll(wrong.GetEntry("random.bin")!.Open()));
        Assert.False(wrong.GetEntry("dir/")?.IsEncrypted ?? false);
    }

    [Fact]
    public void Aes_Versions()
    {
        // bsdtar writes AE-2 (no CRC) for the files of less than 20 bytes, and AE-1 for the others
        using var archive = new ZipArchive(ZipTestData.OpenRead("aes-bsdtar.zip"), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = "password" }, compressionOptions: null);
        Assert.Null(archive.GetEntry("hello.txt")!.Crc32);
        Assert.Equal(0x250CC74Eu, archive.GetEntry("lorem.txt")!.Crc32);
        Assert.Equal(ZipCompressionMethod.Deflate, archive.GetEntry("lorem.txt")!.CompressionMethod);
    }

    [Fact]
    public void Aes_CorruptedAuthenticationCode()
    {
        var data = ZipTestData.ReadBytes("aes256-stored.zip");
        using (var archive = new ZipArchive(new MemoryStream(data)))
        {
            // The last byte of the content of lorem.txt is the last byte of its authentication code
            var entry = archive.GetEntry("lorem.txt")!;
            data[FindDataStart(data, entry) + (int)entry.CompressedLength - 1] ^= 1;
        }

        using var corrupted = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = "password" }, compressionOptions: null);
        Assert.Throws<InvalidDataException>(() => ZipTestData.ReadAll(corrupted.GetEntry("lorem.txt")!.Open()));
        Assert.Equal(ZipTestData.GetExpectedHash("hello.txt"), ZipTestData.Hash(ZipTestData.ReadAll(corrupted.GetEntry("hello.txt")!.Open())));
    }

    [Fact]
    public void Stored_CorruptedContent()
    {
        var data = ZipTestData.ReadBytes("stored.zip");
        int offset;
        using (var archive = new ZipArchive(new MemoryStream(data)))
        {
            offset = FindDataStart(data, archive.GetEntry("lorem.txt")!);
        }

        data[offset + 100] ^= 0x20;
        using var corrupted = new ZipArchive(new MemoryStream(data));
        var exception = Assert.Throws<InvalidDataException>(() => ZipTestData.ReadAll(corrupted.GetEntry("lorem.txt")!.Open()));
        Assert.Contains("CRC-32", exception.Message);
    }

    [Fact]
    public void Deflate_CorruptedContent()
    {
        var data = ZipTestData.ReadBytes("deflate.zip");
        int offset;
        using (var archive = new ZipArchive(new MemoryStream(data)))
        {
            offset = FindDataStart(data, archive.GetEntry("random.bin")!);
        }

        for (var i = 0; i < 64; i++)
        {
            data[offset + 10 + i] = 0xFF;
        }

        using var corrupted = new ZipArchive(new MemoryStream(data));
        Assert.Throws<InvalidDataException>(() => ZipTestData.ReadAll(corrupted.GetEntry("random.bin")!.Open()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(21)]
    [InlineData(100)]
    public void Truncated(int length)
    {
        var data = ZipTestData.ReadBytes("deflate.zip").AsSpan(0, length).ToArray();
        Assert.Throws<InvalidDataException>(() => new ZipArchive(new MemoryStream(data)));
    }

    [Fact]
    public void TruncatedContent()
    {
        // The compressed size goes past the end of the archive
        var data = ZipTestData.ReadBytes("stored.zip");
        PatchEntry(data, "random.bin", (header, isCentral) => BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(isCentral ? 20 : 18), 0x7FFFFFFF));
        using var archive = new ZipArchive(new MemoryStream(data));
        Assert.Throws<InvalidDataException>(() => archive.GetEntry("random.bin")!.Open());
    }

    [Fact]
    public void WrongUncompressedSize()
    {
        var data = ZipTestData.ReadBytes("deflate.zip");
        PatchEntry(data, "lorem.txt", (header, isCentral) => BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(isCentral ? 24 : 22), 3000));
        using var archive = new ZipArchive(new MemoryStream(data));
        var exception = Assert.Throws<InvalidDataException>(() => ZipTestData.ReadAll(archive.GetEntry("lorem.txt")!.Open()));
        Assert.Contains("3000", exception.Message);
    }

    [Fact]
    public void NotAZipArchive()
    {
        Assert.Throws<InvalidDataException>(() => new ZipArchive(new MemoryStream(new byte[1000])));
    }

    [Fact]
    public void EntryCountLargerThanCentralDirectory()
    {
        var data = ZipTestData.ReadBytes("stored.zip");
        var eocd = data.AsSpan().LastIndexOf("PK\u0005\u0006"u8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(eocd + 8), 60000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(eocd + 10), 60000);
        Assert.Throws<InvalidDataException>(() => new ZipArchive(new MemoryStream(data)));
    }

    [Fact]
    public void UnsupportedMethod()
    {
        var data = ZipTestData.ReadBytes("stored.zip");
        PatchEntry(data, "hello.txt", (header, isCentral) => BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(isCentral ? 10 : 8), 97));
        using var archive = new ZipArchive(new MemoryStream(data));
        var entry = archive.GetEntry("hello.txt")!;
        Assert.Equal(ZipCompressionMethod.WavPack, entry.CompressionMethod);
        var exception = Assert.Throws<NotSupportedException>(() => entry.Open());
        Assert.Contains("97", exception.Message);
    }

    [Fact]
    public void StrongEncryption()
    {
        var data = ZipTestData.ReadBytes("stored.zip");
        PatchEntry(data, "hello.txt", (header, isCentral) => BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(isCentral ? 8 : 6), 0x41));
        using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = "password" }, compressionOptions: null);
        var entry = archive.GetEntry("hello.txt")!;
        Assert.Equal(ZipEncryption.Unsupported, entry.EncryptionMethod);
        Assert.Throws<NotSupportedException>(() => entry.Open());
    }

    [Fact]
    public void Comments()
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead("comments.zip"));
        Assert.Equal("The archive comment", archive.Comment);
        Assert.Equal("The entry comment", archive.GetEntry("hello.txt")!.Comment);
    }

    [Fact]
    public void Names()
    {
        using var utf8 = new ZipArchive(ZipTestData.OpenRead("utf8-names.zip"));
        Assert.Equal(["café/日本.txt", "naïve.txt"], utf8.Entries.Select(entry => entry.FullName));

        // An MS-DOS name in the code page 437 with backslashes, and a name in the Unicode path extra field
        using var cp437 = new ZipArchive(ZipTestData.OpenRead("cp437-names.zip"));
        Assert.Equal(["résumé/café.txt", "日本語.txt"], cp437.Entries.Select(entry => entry.FullName));
        Assert.Equal(FileAttributes.Archive, cp437.Entries[0].Attributes);

        using var latin1 = new ZipArchive(ZipTestData.OpenRead("cp437-names.zip"), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { EntryNameEncoding = System.Text.Encoding.Latin1 }, compressionOptions: null);
        Assert.Equal("r\u0082sum\u0082/caf\u0082.txt", latin1.Entries[0].FullName);
    }

    [Fact]
    public void SymbolicLink()
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead("symlink.zip"));
        var link = archive.GetEntry("link")!;
        Assert.Equal("hello.txt", link.LinkTarget);
        Assert.Null(archive.GetEntry("hello.txt")!.LinkTarget);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, archive.GetEntry("hello.txt")!.UnixFileMode);
        Assert.Null(archive.GetEntry("hello.txt")!.Attributes);
        Assert.Equal(3, archive.GetEntry("hello.txt")!.VersionMadeBy >> 8);
    }

    [Fact]
    public void Timestamps()
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead("timestamps.zip"));
        var ntfs = archive.GetEntry("ntfs.txt")!;
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), ntfs.LastWriteTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000100), ntfs.LastAccessTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1600000000), ntfs.CreationTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1650000000), archive.GetEntry("unix.txt")!.LastWriteTime);
        var dos = archive.GetEntry("dos.txt")!;
        Assert.Equal(new DateTimeOffset(new DateTime(2010, 6, 15, 13, 45, 30, DateTimeKind.Unspecified)), dos.LastWriteTime);
        Assert.Null(dos.CreationTime);
    }

    [Fact]
    public void Zip64()
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead("zip64-eocd.zip"));
        Assert.Equal("zip64", archive.Comment);
        Assert.Equal(2511L, archive.GetEntry("lorem.txt")!.Length);
    }

    [Fact]
    public void SelfExtractingArchive()
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead("sfx.zip"));
        using var expected = new ZipArchive(ZipTestData.OpenRead("deflate.zip"));
        Assert.Equal(expected.Entries.Select(entry => entry.FullName), archive.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public void Empty()
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead("empty.zip"));
        Assert.Empty(archive.Entries);
    }

    [Theory]
    [InlineData("split-infozip.zip")]
    [InlineData("split-infozip.z01")]
    [InlineData("split7z.zip.001")]
    public void SplitArchive_Open(string fileName)
    {
        using var archive = ZipFile.OpenRead(ZipTestData.GetPath(fileName));
        foreach (var entry in archive.Entries)
        {
            Assert.Equal(ZipTestData.GetExpectedHash(entry.FullName), ZipTestData.Hash(ZipTestData.ReadAll(entry.Open())));
        }

        Assert.NotEmpty(archive.Entries);
    }

    [Fact]
    public void SplitArchive_VolumeResolver()
    {
        var options = new ArchiveReaderOptions { VolumeResolver = index => index is 1 ? ZipTestData.OpenRead("split-infozip.zip") : null };
        using var archive = new ZipArchive(ZipTestData.OpenRead("split-infozip.z01"), ZipArchiveMode.Read, leaveOpen: false, options, compressionOptions: null);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal(ZipTestData.GetExpectedHash("big.bin"), ZipTestData.Hash(ZipTestData.ReadAll(entry.Open())));
    }

    [Fact]
    public void SplitArchive_LastVolumeOnly()
    {
        Assert.Throws<InvalidDataException>(() => new ZipArchive(ZipTestData.OpenRead("split-infozip.zip")));
    }

    [Fact]
    public void ExtractToDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var archive = new ZipArchive(ZipTestData.OpenRead("deflate.zip"));
            archive.ExtractToDirectory(directory.FullName);
            Assert.Equal(ZipTestData.GetExpectedHash("dir/nested.txt"), ZipTestData.Hash(File.ReadAllBytes(Path.Combine(directory.FullName, "dir", "nested.txt"))));
            Assert.Equal(ZipTestData.GetExpectedHash("empty.txt"), ZipTestData.Hash(File.ReadAllBytes(Path.Combine(directory.FullName, "empty.txt"))));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void NonSeekableStream()
    {
        Assert.Throws<ArgumentException>(() => new ZipArchive(ZipTestData.CreateForwardOnlyStream(ZipTestData.ReadBytes("stored.zip"), 100)));
    }

    [Fact]
    public void EntriesCanBeReadAlternately()
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead("deflate.zip"));
        using var lorem = archive.GetEntry("lorem.txt")!.Open();
        using var random = archive.GetEntry("random.bin")!.Open();
        var loremContent = new MemoryStream();
        var randomContent = new MemoryStream();
        var buffer = new byte[7];
        int read1, read2;
        do
        {
            read1 = lorem.Read(buffer);
            loremContent.Write(buffer, 0, read1);
            read2 = random.Read(buffer);
            randomContent.Write(buffer, 0, read2);
        }
        while (read1 > 0 || read2 > 0);

        Assert.Equal(ZipTestData.GetExpectedHash("lorem.txt"), ZipTestData.Hash(loremContent.ToArray()));
        Assert.Equal(ZipTestData.GetExpectedHash("random.bin"), ZipTestData.Hash(randomContent.ToArray()));
    }

    private static int FindDataStart(byte[] data, ArchiveEntry entry)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(entry.FullName);
        var index = 0;
        while (true)
        {
            var found = data.AsSpan(index).IndexOf("PK\u0003\u0004"u8);
            Assert.True(found >= 0);
            var header = index + found;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(header + 26));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(header + 28));
            if (data.AsSpan(header + 30, nameLength).SequenceEqual(name))
                return header + 30 + nameLength + extraLength;

            index = header + 4;
        }
    }

    private static void PatchEntry(byte[] data, string name, PatchHeader patch)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var patched = 0;
        for (var i = 0; i + 46 <= data.Length; i++)
        {
            if (data.AsSpan(i, 4).SequenceEqual("PK\u0003\u0004"u8) && data.AsSpan(i + 30).StartsWith(nameBytes) && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 26)) == nameBytes.Length)
            {
                patch(data.AsSpan(i), isCentral: false);
                patched++;
            }
            else if (data.AsSpan(i, 4).SequenceEqual("PK\u0001\u0002"u8) && data.AsSpan(i + 46).StartsWith(nameBytes) && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 28)) == nameBytes.Length)
            {
                patch(data.AsSpan(i), isCentral: true);
                patched++;
            }
        }

        Assert.Equal(2, patched);
    }

    private delegate void PatchHeader(Span<byte> header, bool isCentral);
}
