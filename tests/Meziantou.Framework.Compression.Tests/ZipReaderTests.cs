namespace Meziantou.Framework.Compression.Tests;

public sealed class ZipReaderTests
{
    [Theory]
    [MemberData(nameof(ZipTestData.Fixtures), MemberType = typeof(ZipTestData))]
    public void ReadEntries(string fileName, string? password, string[] expectedFiles)
    {
        foreach (var maxRead in new[] { 1, 7, 100_000 })
        {
            var options = new ArchiveReaderOptions { Password = password };
            using var reader = ZipArchive.CreateForwardReader(new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes(fileName), maxRead), options, leaveOpen: false);
            Assert.Equal(ArchiveFormat.Zip, reader.Format);
            var files = new List<string>();
            while (reader.MoveToNextEntry())
            {
                var entry = Assert.IsType<ZipArchiveEntry>(reader.Entry);
                if (entry.IsDirectory)
                    continue;

                files.Add(entry.FullName);
                var content = ZipTestData.ReadAll(reader.OpenEntryStream());
                Assert.Equal(ZipTestData.GetExpectedHash(entry.FullName), ZipTestData.Hash(content));
                Assert.Equal(content.Length, entry.Length);
            }

            Assert.Null(reader.Entry);
            Assert.False(reader.MoveToNextEntry());
            Assert.Equal(expectedFiles.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(ZipTestData.Fixtures), MemberType = typeof(ZipTestData))]
    public void MatchesRandomAccess(string fileName, string? password, string[] expectedFiles)
    {
        _ = expectedFiles;
        var options = new ArchiveReaderOptions { Password = password };
        using var archive = new ZipArchive(ZipTestData.OpenRead(fileName), System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false, options, compressionOptions: null);
        using var reader = ZipArchive.CreateForwardReader(new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes(fileName), 4096), options, leaveOpen: false);
        var index = 0;
        while (reader.MoveToNextEntry())
        {
            var entry = (ZipArchiveEntry)reader.Entry!;
            var expected = archive.Entries[index++];
            Assert.Equal(expected.FullName, entry.FullName);
            Assert.Equal(expected.IsDirectory, entry.IsDirectory);
            Assert.Equal(expected.CompressionMethod, entry.CompressionMethod);
            Assert.Equal(expected.EncryptionMethod, entry.EncryptionMethod);
            Assert.Equal(expected.IsEncrypted, entry.IsEncrypted);
            Assert.Equal(ZipTestData.ReadAll(expected.Open()), ZipTestData.ReadAll(entry.Open()));

            // Once the content is read, the data descriptor gives the sizes of a streamed entry
            Assert.Equal(expected.Length, entry.Length);
            Assert.Equal(expected.CompressedLength, entry.CompressedLength);
            Assert.Equal(expected.Crc32, entry.Crc32);
        }

        Assert.HasCount(index, archive.Entries);
    }

    [Theory]
    [MemberData(nameof(ZipTestData.Fixtures), MemberType = typeof(ZipTestData))]
    public void SkipEntries(string fileName, string? password, string[] expectedFiles)
    {
        _ = password;
        using var archive = new ZipArchive(ZipTestData.OpenRead(fileName));
        using var reader = ZipArchive.CreateForwardReader(new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes(fileName), 100), options: null, leaveOpen: false);
        var names = new List<string>();
        while (reader.MoveToNextEntry())
        {
            names.Add(reader.Entry!.FullName);
        }

        Assert.Equal(archive.Entries.Select(entry => entry.FullName), names);
        Assert.HasCountGreaterThanOrEqual(expectedFiles.Length, names);
    }

    [Theory]
    [InlineData("deflate.zip")]
    [InlineData("descriptor-python.zip")]
    [InlineData("descriptor-stored.zip")]
    [InlineData("streamed-infozip.zip")]
    [InlineData("aes-bsdtar.zip")]
    [InlineData("zipcrypto-bsdtar.zip")]
    public void PartialRead(string fileName)
    {
        using var archive = new ZipArchive(ZipTestData.OpenRead(fileName));
        using var reader = ZipArchive.CreateForwardReader(new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes(fileName), 100), new ArchiveReaderOptions { Password = "password" }, leaveOpen: false);
        var count = 0;
        while (reader.MoveToNextEntry())
        {
            count++;
            if (reader.Entry!.IsDirectory)
                continue;

            var stream = reader.OpenEntryStream();
            _ = stream.ReadByte();
            Assert.Throws<InvalidOperationException>(() => reader.OpenEntryStream());
        }

        Assert.HasCount(count, archive.Entries);
    }

    [Fact]
    public async Task ReadEntriesAsync()
    {
        await using var reader = ZipArchive.CreateForwardReader(new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes("descriptor-python.zip"), 100), options: null, leaveOpen: false);
        var names = new List<string>();
        while (await reader.MoveToNextEntryAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.Entry!.FullName);
            await using var stream = await reader.Entry.OpenAsync(TestContext.Current.CancellationToken);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
            Assert.Equal(ZipTestData.GetExpectedHash(reader.Entry.FullName), ZipTestData.Hash(copy.ToArray()));
        }

        Assert.Equal(["hello.txt", "lorem.txt", "random.bin", "empty.txt", "dir/nested.txt"], names);
    }

    [Fact]
    public void StreamedEntry_SizesKnownAfterReading()
    {
        using var reader = ZipArchive.CreateForwardReader(ZipTestData.OpenRead("descriptor-python.zip"), options: null, leaveOpen: false);
        Assert.True(reader.MoveToNextEntry());
        var entry = (ZipArchiveEntry)reader.Entry!;
        Assert.Equal(-1L, entry.Length);
        Assert.Equal(-1L, entry.CompressedLength);
        Assert.Null(entry.Crc32);
        ZipTestData.ReadAll(entry.Open());
        Assert.Equal(14L, entry.Length);
        Assert.Equal(16L, entry.CompressedLength);
        Assert.Equal(0x7B55A718u, entry.Crc32);
    }

    [Fact]
    public void ReadAfterMove()
    {
        using var reader = ZipArchive.CreateForwardReader(ZipTestData.OpenRead("deflate.zip"), options: null, leaveOpen: false);
        Assert.True(reader.MoveToNextEntry());
        Assert.True(reader.MoveToNextEntry());
        var entry = reader.Entry!;
        using var stream = reader.OpenEntryStream();
        Assert.True(reader.MoveToNextEntry());
        Assert.Throws<InvalidOperationException>(() => stream.ReadByte());
        Assert.Throws<InvalidOperationException>(() => entry.Open());
    }

    [Fact]
    public void NoCurrentEntry()
    {
        using var reader = ZipArchive.CreateForwardReader(ZipTestData.OpenRead("deflate.zip"), options: null, leaveOpen: false);
        Assert.Throws<InvalidOperationException>(() => reader.OpenEntryStream());
    }

    [Theory]
    [InlineData("split-infozip.z01", "split-infozip.zip", null)]
    [InlineData("split7z.zip.001", "split7z.zip.002", "split7z.zip.003")]
    public void SplitArchive(string first, string second, string? third)
    {
        var volumes = new[] { first, second, third };
        var options = new ArchiveReaderOptions { VolumeResolver = index => index < volumes.Length && volumes[index] is { } name ? new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes(name), 1000) : null };
        using var reader = ZipArchive.CreateForwardReader(new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes(first), 1000), options, leaveOpen: false);
        var count = 0;
        while (reader.MoveToNextEntry())
        {
            count++;
            Assert.Equal(ZipTestData.GetExpectedHash(reader.Entry!.FullName), ZipTestData.Hash(ZipTestData.ReadAll(reader.OpenEntryStream())));
        }

        Assert.NotEqual(0, count);
    }

    [Theory]
    [InlineData("deflate.zip")]
    [InlineData("descriptor-python.zip")]
    [InlineData("descriptor-stored.zip")]
    [InlineData("aes-bsdtar.zip")]
    public void Truncated(string fileName)
    {
        var data = ZipTestData.ReadBytes(fileName);
        foreach (var length in new[] { 0, 3, 20, 40, 200, data.Length / 2 })
        {
            Assert.Throws<InvalidDataException>(() =>
            {
                using var reader = ZipArchive.CreateForwardReader(new MemoryStream(data, 0, length), new ArchiveReaderOptions { Password = "password" }, leaveOpen: false);
                while (reader.MoveToNextEntry())
                {
                    ZipTestData.ReadAll(reader.OpenEntryStream());
                }
            });
        }
    }

    [Fact]
    public void StreamedDeflate_TruncatedData()
    {
        // The deflate decoder of a streamed entry does not find the end of its data
        var data = ZipTestData.ReadBytes("descriptor-python.zip");
        var start = 30 + "hello.txt".Length;
        var truncated = data.AsSpan(0, start + 5).ToArray();
        using var reader = ZipArchive.CreateForwardReader(new MemoryStream(truncated), options: null, leaveOpen: false);
        Assert.True(reader.MoveToNextEntry());
        Assert.Throws<InvalidDataException>(() => ZipTestData.ReadAll(reader.OpenEntryStream()));
    }

    [Fact]
    public void WrongPassword()
    {
        using var reader = ZipArchive.CreateForwardReader(ZipTestData.OpenRead("aes-bsdtar.zip"), new ArchiveReaderOptions { Password = "wrong" }, leaveOpen: false);
        Assert.True(reader.MoveToNextEntry());
        Assert.Throws<ArchivePasswordException>(() => reader.OpenEntryStream());

        // The entry is skipped
        Assert.True(reader.MoveToNextEntry());
        Assert.Equal("lorem.txt", reader.Entry!.FullName);
    }

    [Fact]
    public void Empty()
    {
        using var reader = ZipArchive.CreateForwardReader(ZipTestData.OpenRead("empty.zip"), options: null, leaveOpen: false);
        Assert.False(reader.MoveToNextEntry());
    }

    [Fact]
    public void NotAZipArchive()
    {
        using var reader = ZipArchive.CreateForwardReader(new MemoryStream(new byte[100]), options: null, leaveOpen: false);
        Assert.Throws<InvalidDataException>(() => reader.MoveToNextEntry());
    }

    [Fact]
    public void ExtractToDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var reader = ZipArchive.CreateForwardReader(new ZipTestData.ForwardOnlyStream(ZipTestData.ReadBytes("streamed-infozip.zip"), 4096), options: null, leaveOpen: false);
            reader.ExtractToDirectory(directory.FullName);
            Assert.Equal(ZipTestData.GetExpectedHash("dir/nested.txt"), ZipTestData.Hash(File.ReadAllBytes(Path.Combine(directory.FullName, "dir", "nested.txt"))));
            Assert.Equal(ZipTestData.GetExpectedHash("random.bin"), ZipTestData.Hash(File.ReadAllBytes(Path.Combine(directory.FullName, "random.bin"))));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
