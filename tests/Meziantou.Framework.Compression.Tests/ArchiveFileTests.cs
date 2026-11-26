using System.IO.Compression;
using System.IO.Pipes;
using System.Security.Cryptography;

namespace Meziantou.Framework.Compression.Tests;

// The files of TestData/Archive are described in TestData/Archive/README.md
public sealed class ArchiveFileTests
{
    private static readonly string[] SampleFiles = ["hello.txt", "docs/lines.txt", "docs/sub/nested.txt"];

    private static string GetPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    public static TheoryData<string, ArchiveFormat, CompressionFormat> DetectionCases => new()
    {
        { "Archive/sample.tar", ArchiveFormat.Tar, CompressionFormat.None },
        { "Archive/sample.tar.gz", ArchiveFormat.Tar, CompressionFormat.GZip },
        { "Archive/sample.tar.bz2", ArchiveFormat.Tar, CompressionFormat.BZip2 },
        { "Archive/sample.tar.xz", ArchiveFormat.Tar, CompressionFormat.Xz },
        { "Archive/sample.tar.lzma", ArchiveFormat.Tar, CompressionFormat.Lzma },
        { "Archive/sample.tar.lz", ArchiveFormat.Tar, CompressionFormat.LZip },
        { "Archive/sample.tar.Z", ArchiveFormat.Tar, CompressionFormat.UnixCompress },
        { "Archive/sample.tar.zst", ArchiveFormat.Tar, CompressionFormat.Zstandard },
        { "Archive/hello.txt.gz", ArchiveFormat.Unknown, CompressionFormat.GZip },
        { "Zip/deflate.zip", ArchiveFormat.Zip, CompressionFormat.None },
        { "Zip/sfx.zip", ArchiveFormat.Zip, CompressionFormat.None },
        { "SevenZip/lzma2.7z", ArchiveFormat.SevenZip, CompressionFormat.None },
        { "Rar/libarchive-rar5-compressed.rar", ArchiveFormat.Rar, CompressionFormat.None },
        { "Rar/libarchive-rar.rar", ArchiveFormat.Rar, CompressionFormat.None },
        { "Archive/README.md", ArchiveFormat.Unknown, CompressionFormat.None },
    };

    public static TheoryData<string> CompressedTars =>
    [
        "Archive/sample.tar",
        "Archive/sample.tar.gz",
        "Archive/sample.tar.bz2",
        "Archive/sample.tar.xz",
        "Archive/sample.tar.lzma",
        "Archive/sample.tar.lz",
        "Archive/sample.tar.Z",
        "Archive/sample.tar.zst",
    ];

    [Theory]
    [MemberData(nameof(DetectionCases))]
    public void DetectFormat(string name, ArchiveFormat format, CompressionFormat compression)
    {
        var info = ArchiveFile.DetectFormat(GetPath(name));
        Assert.Equal(new ArchiveFormatInfo(format, compression), info);
    }

    [Fact]
    public void DetectFormat_RestoresPosition()
    {
        using var stream = File.OpenRead(GetPath("Archive/sample.tar.xz"));
        stream.Position = 0;
        _ = ArchiveFile.DetectFormat(stream);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void DetectFormat_RequiresSeekableStream()
    {
        using var stream = ZipTestData.CreateForwardOnlyStream(File.ReadAllBytes(GetPath("Archive/sample.tar")), 512);
        Assert.Throws<ArgumentException>("stream", () => ArchiveFile.DetectFormat(stream));
    }

    [Theory]
    [MemberData(nameof(CompressedTars))]
    public void Open_Tar(string name)
    {
        using var archive = ArchiveFile.Open(GetPath(name));
        Assert.Equal(ArchiveFormat.Tar, archive.Format);
        AssertSample(archive.Entries, readTwice: true);
    }

    [Theory]
    [MemberData(nameof(CompressedTars))]
    public void ArchiveReader_Tar_NonSeekable(string name)
    {
        using var reader = ArchiveReader.Create(ZipTestData.CreateForwardOnlyStream(File.ReadAllBytes(GetPath(name)), 7));
        Assert.Equal(ArchiveFormat.Tar, reader.Format);
        var entries = new List<ArchiveEntry>();
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry!;
            entries.Add(entry);
            if (!entry.IsDirectory && entry.LinkTarget is null)
            {
                using var stream = reader.OpenEntryStream();
                contents[entry.FullName] = ReadAll(stream);
            }
        }

        Assert.False(reader.MoveToNextEntry());
        Assert.Null(reader.Entry);
        AssertSampleEntries(entries);
        Assert.HasCount(3, contents);
        Assert.Equal("Hello, archive!\n"u8.ToArray(), contents["hello.txt"]);
    }

    [Fact]
    public async Task ArchiveReader_Tar_Async_SkipsUnreadContent()
    {
        await using var reader = ArchiveReader.Create(ZipTestData.CreateForwardOnlyStream(File.ReadAllBytes(GetPath("Archive/sample.tar.gz")), 100));
        var names = new List<string>();
        while (await reader.MoveToNextEntryAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.Entry!.FullName);
        }

        Assert.Equal(["hello.txt", "docs/", "docs/lines.txt", "docs/sub/", "docs/sub/nested.txt", "link-to-hello"], names);
    }

    [Fact]
    public void ArchiveReader_StreamOfPreviousEntryIsInvalid()
    {
        using var reader = ArchiveReader.Create(GetPath("Archive/sample.tar"));
        Assert.True(reader.MoveToNextEntry());
        var first = reader.Entry!;
        Assert.True(reader.MoveToNextEntry());
        Assert.Throws<InvalidOperationException>(() => first.Open());
    }

    [Fact]
    public void ArchiveReader_OpenEntryStream_BeforeFirstEntry()
    {
        using var reader = ArchiveReader.Create(GetPath("Archive/sample.tar"));
        Assert.Throws<InvalidOperationException>(() => reader.OpenEntryStream());
    }

    [Theory]
    [InlineData("Zip/deflate.zip", ArchiveFormat.Zip)]
    [InlineData("Rar/libarchive-rar5-compressed.rar", ArchiveFormat.Rar)]
    [InlineData("Rar/libarchive-rar.rar", ArchiveFormat.Rar)]
    public void ArchiveReader_AgreesWithArchiveFile(string name, ArchiveFormat format)
    {
        var bytes = File.ReadAllBytes(GetPath(name));
        using var archive = ArchiveFile.Open(new MemoryStream(bytes, writable: false));
        Assert.Equal(format, archive.Format);
        var expected = archive.Entries.Select(entry => (entry.FullName, entry.IsDirectory ? "" : Hash(entry))).ToList();

        using var reader = ArchiveReader.Create(ZipTestData.CreateForwardOnlyStream(bytes, 333));
        Assert.Equal(format, reader.Format);
        var actual = new List<(string, string)>();
        while (reader.MoveToNextEntry())
        {
            actual.Add((reader.Entry!.FullName, reader.Entry.IsDirectory ? "" : Hash(reader.Entry)));
        }

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(CompressedTars))]
    [InlineData("Zip/deflate.zip")]
    [InlineData("Zip/descriptor-python.zip")]
    [InlineData("Zip/streamed-infozip.zip")]
    [InlineData("Rar/libarchive-rar5-compressed.rar")]
    [InlineData("Rar/libarchive-rar.rar")]
    public async Task ArchiveReader_NonSeekableStream_Async(string name)
    {
        var bytes = File.ReadAllBytes(GetPath(name));
        using var archive = ArchiveFile.Open(new MemoryStream(bytes, writable: false));
        var expected = archive.Entries.Select(entry => (entry.FullName, entry.IsDirectory ? "" : Hash(entry))).ToList();

        // Every read returns one byte, as a slow network stream may
        await using var reader = ArchiveReader.Create(ZipTestData.CreateForwardOnlyStream(bytes, 1));
        var actual = new List<(string, string)>();
        while (await reader.MoveToNextEntryAsync(TestContext.Current.CancellationToken))
        {
            var entry = reader.Entry!;
            var hash = "";
            if (!entry.IsDirectory)
            {
                var stream = await entry.OpenAsync(TestContext.Current.CancellationToken);
                await using (stream)
                {
                    using var content = new MemoryStream();
                    await stream.CopyToAsync(content, TestContext.Current.CancellationToken);
                    hash = Convert.ToHexString(SHA256.HashData(content.ToArray()));
                }
            }

            actual.Add((entry.FullName, hash));
        }

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Archive/sample.tar")]
    [InlineData("Archive/sample.tar.xz")]
    [InlineData("Zip/deflate.zip")]
    [InlineData("SevenZip/lzma2.7z")]
    [InlineData("Rar/libarchive-rar5-compressed.rar")]
    public void Open_NonSeekableStream_Throws(string name)
    {
        // Random access needs a seekable stream: ArchiveReader reads the stream forward only
        using var stream = ZipTestData.CreateForwardOnlyStream(File.ReadAllBytes(GetPath(name)), 1000);
        Assert.Throws<ArgumentException>("stream", () => ArchiveFile.Open(stream, leaveOpen: true));
    }

    [Fact]
    public void ArchiveReader_SevenZip_NotSupported()
    {
        using var stream = File.OpenRead(GetPath("SevenZip/lzma2.7z"));
        Assert.Throws<NotSupportedException>(() => ArchiveReader.Create(stream, leaveOpen: true));
    }

    [Fact]
    public void ArchiveReader_SeekableStream_LeaveOpen()
    {
        using var stream = File.OpenRead(GetPath("Zip/deflate.zip"));
        using (var reader = ArchiveReader.Create(stream, leaveOpen: true))
        {
            Assert.True(reader.MoveToNextEntry());
        }

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Open_SevenZipAndRarAndZip()
    {
        using var sevenZip = ArchiveFile.Open(GetPath("SevenZip/lzma2.7z"));
        Assert.IsType<SevenZipArchive>(sevenZip);
        using var rar = ArchiveFile.Open(GetPath("Rar/libarchive-rar5-compressed.rar"));
        Assert.IsType<RarArchive>(rar);
        using var zip = ArchiveFile.Open(GetPath("Zip/deflate.zip"));
        Assert.IsType<ZipArchive>(zip);
    }

    [Fact]
    public void Open_MultiVolumeFromPath()
    {
        using var archive = ArchiveFile.Open(GetPath("SevenZip/volumes.7z.001"));
        Assert.Equal(ArchiveFormat.SevenZip, archive.Format);
        Assert.NotEmpty(archive.Entries);
    }

    [Fact]
    public void Open_CompressedFileThatIsNotATar()
    {
        var exception = Assert.Throws<InvalidDataException>(() => ArchiveFile.Open(GetPath("Archive/hello.txt.gz")));
        Assert.Contains("GZip", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_UnknownData()
    {
        Assert.Throws<InvalidDataException>(() => ArchiveFile.Open(GetPath("Archive/README.md")));
        Assert.Throws<InvalidDataException>(() => ArchiveReader.Create(GetPath("Archive/README.md")));
    }

    [Fact]
    public void EmptyZip64Archive()
    {
        // The archive is its zip64 end of central directory records
        using var zip = new MemoryStream();
        using (new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, new ZipCompressionOptions { Zip64 = Zip64Mode.Always }))
        {
        }

        Assert.Equal(ArchiveFormat.Zip, ArchiveFile.DetectFormat(new MemoryStream(zip.ToArray())).Format);
        using var reader = ArchiveReader.Create(new MemoryStream(zip.ToArray()));
        Assert.Equal(ArchiveFormat.Zip, reader.Format);
        Assert.False(reader.MoveToNextEntry());
    }

    [Fact]
    public void Tar_TruncatedContent_Throws()
    {
        using var tar = new MemoryStream();
        using (var writer = new System.Formats.Tar.TarWriter(tar, leaveOpen: true))
        {
            writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "data.bin") { DataStream = new MemoryStream(new byte[2000]) });
        }

        // The archive ends in the middle of the content of the entry
        var data = tar.ToArray();
        var truncated = data.AsSpan(0, data.Length - 1024 - 1024).ToArray();

        Assert.Throws<InvalidDataException>(() => ArchiveFile.Open(new MemoryStream(truncated)));

        using var reader = ArchiveReader.Create(new MemoryStream(truncated));
        Assert.True(reader.MoveToNextEntry());
        using var content = reader.OpenEntryStream();
        Assert.Throws<InvalidDataException>(() => content.CopyTo(Stream.Null));
    }

    [Fact]
    public void Tar_ExtendedAttributesLargerThanAllowed_Throws()
    {
        using var tar = new MemoryStream();
        using (var writer = new System.Formats.Tar.TarWriter(tar, leaveOpen: true))
        {
            writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "a.txt", new Dictionary<string, string>(StringComparer.Ordinal) { ["comment"] = "x" }) { DataStream = new MemoryStream("A"u8.ToArray()) });
        }

        // The size field of the extended attributes header is the largest value it can hold, and its checksum is updated
        var data = tar.ToArray();
        "77777777777\0"u8.CopyTo(data.AsSpan(124));
        "        "u8.CopyTo(data.AsSpan(148));
        var checksum = 0;
        foreach (var value in data.AsSpan(0, 512))
        {
            checksum += value;
        }

        System.Text.Encoding.ASCII.GetBytes(Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ").CopyTo(data, 148);

        Assert.Throws<InvalidDataException>(() => ArchiveFile.Open(new MemoryStream(data)));
        using var reader = ArchiveReader.Create(new MemoryStream(data));
        Assert.Throws<InvalidDataException>(() => reader.MoveToNextEntry());
    }

    [Theory]
    [InlineData("17948023876270295701")]
    [InlineData("-17948023876270295701")]
    public void Tar_TimeOutOfTheRangeOfADate_Throws(string time)
    {
        // A number of seconds larger than a 64-bit integer. The writer reads the times of the attributes and writes its own
        // mtime: the time is written as another attribute, and the names of the two attributes are then changed
        using var tar = new MemoryStream();
        using (var writer = new System.Formats.Tar.TarWriter(tar, leaveOpen: true))
        {
            writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "a.txt", new Dictionary<string, string>(StringComparer.Ordinal) { ["xtime"] = time }) { DataStream = new MemoryStream("A"u8.ToArray()) });
        }

        var data = tar.ToArray();
        "z"u8.CopyTo(data.AsSpan(data.AsSpan().IndexOf(" mtime="u8) + 1));
        "m"u8.CopyTo(data.AsSpan(data.AsSpan().IndexOf(" xtime="u8) + 1));

        Assert.Throws<InvalidDataException>(() => ArchiveFile.Open(new MemoryStream(data)));
        using var reader = ArchiveReader.Create(new MemoryStream(data));
        Assert.Throws<InvalidDataException>(() => reader.MoveToNextEntry());
    }

    [Fact]
    public void Open_LeaveOpen()
    {
        using var stream = File.OpenRead(GetPath("Archive/sample.tar.bz2"));
        using (var archive = ArchiveFile.Open(stream, leaveOpen: true))
        {
            Assert.HasCount(6, archive.Entries);
        }

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ExtractToDirectory_Tar()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            ArchiveFile.ExtractToDirectory(GetPath("Archive/sample.tar.xz"), directory.FullName);
            Assert.Equal("Hello, archive!\n", File.ReadAllText(Path.Combine(directory.FullName, "hello.txt")));
            Assert.Equal("nested\n", File.ReadAllText(Path.Combine(directory.FullName, "docs", "sub", "nested.txt")));
            Assert.Equal(new DateTime(2024, 1, 2, 8, 4, 5, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(directory.FullName, "hello.txt")));
            var link = new FileInfo(Path.Combine(directory.FullName, "link-to-hello"));
            Assert.Equal("hello.txt", link.LinkTarget);
            Assert.Throws<IOException>(() => ArchiveFile.ExtractToDirectory(GetPath("Archive/sample.tar.xz"), directory.FullName));
            ArchiveFile.ExtractToDirectory(GetPath("Archive/sample.tar.xz"), directory.FullName, overwriteFiles: true);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_HardLink()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await ArchiveFile.ExtractToDirectoryAsync(GetPath("Archive/hardlink.tar"), directory.FullName, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("shared content\n", await File.ReadAllTextAsync(Path.Combine(directory.FullName, "copy.txt"), TestContext.Current.CancellationToken));

            using var reader = ArchiveReader.Create(GetPath("Archive/hardlink.tar"));
            var target = Path.Combine(directory.FullName, "forward");
            reader.ExtractToDirectory(target);
            Assert.Equal("shared content\n", await File.ReadAllTextAsync(Path.Combine(target, "copy.txt"), TestContext.Current.CancellationToken));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("Archive/traversal.tar")]
    [InlineData("Archive/symlink-escape.tar")]
    [InlineData("Archive/symlink-chain.tar")]
    public void ExtractToDirectory_RejectsPathsOutsideOfTheDirectory(string name)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var destination = Path.Combine(directory.FullName, "out");
            Assert.Throws<IOException>(() => ArchiveFile.ExtractToDirectory(GetPath(name), destination));
            Assert.False(File.Exists(Path.Combine(directory.FullName, "evil.txt")));

            using var reader = ArchiveReader.Create(GetPath(name));
            Assert.Throws<IOException>(() => reader.ExtractToDirectory(destination, overwriteFiles: true));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExtractToDirectory_ReplacesALinkInsteadOfWritingToItsTarget()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var outside = Path.Combine(directory.FullName, "outside.txt");
            File.WriteAllText(outside, "unchanged");
            var destination = Directory.CreateDirectory(Path.Combine(directory.FullName, "out")).FullName;
            File.CreateSymbolicLink(Path.Combine(destination, "hello.txt"), outside);

            Assert.Throws<IOException>(() => ArchiveFile.ExtractToDirectory(GetPath("Archive/sample.tar"), destination));
            ArchiveFile.ExtractToDirectory(GetPath("Archive/sample.tar"), destination, overwriteFiles: true);

            Assert.Equal("unchanged", File.ReadAllText(outside));
            Assert.Null(new FileInfo(Path.Combine(destination, "hello.txt")).LinkTarget);
            Assert.Equal("Hello, archive!\n", File.ReadAllText(Path.Combine(destination, "hello.txt")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DetectFormat_ExecutableThatContainsTheEndOfCentralDirectorySignature()
    {
        // "MZ", then data that contain "PK\u0005\u0006" without being the end of central directory record that ends a zip file
        byte[] data = [(byte)'M', (byte)'Z', .. new byte[100], (byte)'P', (byte)'K', 5, 6, .. new byte[100]];
        using var stream = new MemoryStream(data);
        Assert.Equal(new ArchiveFormatInfo(ArchiveFormat.Unknown, CompressionFormat.None), ArchiveFile.DetectFormat(stream));
    }

    [Theory]
    [InlineData("./")]
    [InlineData(".")]
    public void ExtractToDirectory_RootEntry(string rootName)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var tar = new MemoryStream();
            using (var writer = new System.Formats.Tar.TarWriter(tar, leaveOpen: true))
            {
                writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Directory, rootName));
                writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "./hello.txt") { DataStream = new MemoryStream("Hello"u8.ToArray()) });
            }

            tar.Position = 0;
            using (var archive = ArchiveFile.Open(tar, leaveOpen: true))
            {
                archive.ExtractToDirectory(directory.FullName, overwriteFiles: true);
            }

            Assert.Equal("Hello", File.ReadAllText(Path.Combine(directory.FullName, "hello.txt")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExtractToDirectory_RejectsASiblingWithTheNameOfTheRootInAnotherCase()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var tar = new MemoryStream();
            using (var writer = new System.Formats.Tar.TarWriter(tar, leaveOpen: true))
            {
                writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "../OUT/evil.txt") { DataStream = new MemoryStream("evil"u8.ToArray()) });
            }

            tar.Position = 0;
            using var archive = ArchiveFile.Open(tar, leaveOpen: true);
            Assert.Throws<IOException>(() => archive.ExtractToDirectory(Path.Combine(directory.FullName, "out")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void MaxMemoryUsage_AppliesToTheDecompressionOfTheTarArchive()
    {
        // xz -6 uses an 8 MiB dictionary
        var options = new ArchiveReaderOptions { MaxMemoryUsage = 1024 * 1024 };
        Assert.Throws<InvalidDataException>(() => ArchiveFile.Open(GetPath("Archive/sample.tar.xz"), options));
        Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = ArchiveReader.Create(GetPath("Archive/sample.tar.xz"), options);
        });

        using var archive = ArchiveFile.Open(GetPath("Archive/sample.tar.xz"), new ArchiveReaderOptions { MaxMemoryUsage = 16 * 1024 * 1024 });
        Assert.HasCount(6, archive.Entries);
    }

    [Fact]
    public void Documentation_Examples()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "sample.tar.gz");
            File.Copy(GetPath("Archive/sample.tar.gz"), path);
            var destination = Path.Combine(directory.FullName, "extracted");

            #region ArchiveFile_Open
            // The format is detected from the content: zip, 7z, RAR, or tar compressed with gzip, bzip2, XZ, ...
            using (var archive = ArchiveFile.Open(path))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory)
                        continue;

                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    Assert.NotNull(text);
                }

                var hello = archive.GetEntry("hello.txt");
                Assert.Equal(16, hello?.Length);
            }

            ArchiveFile.ExtractToDirectory(path, destination);
            #endregion

            Assert.True(File.Exists(Path.Combine(destination, "docs", "lines.txt")));

            #region ArchiveReader_ForwardOnly
            // The stream does not need to be seekable: a network response, a pipe, ...
            using var input = File.OpenRead(path);
            using var archiveReader = ArchiveReader.Create(input);
            Assert.Equal(ArchiveFormat.Tar, archiveReader.Format);
            Assert.Equal(CompressionFormat.GZip, archiveReader.CompressionFormat);
            while (archiveReader.MoveToNextEntry())
            {
                var entry = archiveReader.Entry!;
                if (entry.FullName.EndsWith(".txt", StringComparison.Ordinal))
                {
                    // The content can be read until the reader moves to the next entry
                    using var content = archiveReader.OpenEntryStream();
                    using var copy = new MemoryStream();
                    content.CopyTo(copy);
                    Assert.Equal(entry.Length, copy.Length);
                }
            }
            #endregion
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Documentation_Pipe()
    {
        #region ArchiveReader_Pipe
        // A pipe is not seekable, like the body of an HTTP request or the standard output of a process
        using var output = new AnonymousPipeServerStream(PipeDirection.Out);
        using var input = new AnonymousPipeClientStream(PipeDirection.In, output.ClientSafePipeHandle);
        var writer = Task.Run(() =>
        {
            // Each entry is compressed into the pipe as it is written
            using var archive = new ZipArchive(output, ZipArchiveMode.Create);
            foreach (var name in new[] { "a.txt", "b.txt" })
            {
                using var stream = new StreamWriter(archive.CreateEntry(name).Open());
                stream.Write($"Content of {name}");
            }
        });

        // The entries are read while the archive is being written
        using var reader = ArchiveReader.Create(input);
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.MoveToNextEntry())
        {
            using var content = new StreamReader(reader.OpenEntryStream());
            contents[reader.Entry!.FullName] = content.ReadToEnd();
        }

        await writer;
        Assert.Equal("Content of b.txt", contents["b.txt"]);
        #endregion
        Assert.HasCount(2, contents);
    }

    private static void AssertSample(IReadOnlyList<ArchiveEntry> entries, bool readTwice)
    {
        AssertSampleEntries(entries);
        foreach (var name in SampleFiles)
        {
            var entry = entries.Single(e => e.FullName == name);
            var first = Read(entry);
            Assert.Equal(entry.Length, first.Length);
            if (readTwice)
            {
                Assert.Equal(first, Read(entry));
            }
        }

        Assert.Equal("Hello, archive!\n"u8.ToArray(), Read(entries.Single(e => e.FullName == "hello.txt")));
    }

    private static void AssertSampleEntries(IReadOnlyList<ArchiveEntry> entries)
    {
        Assert.Equal(["hello.txt", "docs/", "docs/lines.txt", "docs/sub/", "docs/sub/nested.txt", "link-to-hello"], entries.Select(e => e.FullName));
        Assert.True(entries[1].IsDirectory);
        Assert.Equal("hello.txt", entries[5].LinkTarget);
        Assert.Equal(21490, entries[2].Length);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 8, 4, 5, TimeSpan.Zero), entries[0].LastWriteTime);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, entries[0].UnixFileMode);
    }

    private static byte[] Read(ArchiveEntry entry)
    {
        using var stream = entry.Open();
        return ReadAll(stream);
    }

    private static string Hash(ArchiveEntry entry) => Convert.ToHexString(SHA256.HashData(Read(entry)));

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
