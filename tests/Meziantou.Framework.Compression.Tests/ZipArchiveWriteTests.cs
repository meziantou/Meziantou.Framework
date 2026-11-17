using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Compression.Internals.Zip;

namespace Meziantou.Framework.Compression.Tests;

public sealed class ZipArchiveWriteTests
{
    private const string Password = "pässword";

    public static TheoryData<ZipCompressionMethod, ZipEncryption, bool> RoundTripCases()
    {
        var data = new TheoryData<ZipCompressionMethod, ZipEncryption, bool>();
        foreach (var method in WritableMethods)
        {
            foreach (var encryption in new[] { ZipEncryption.None, ZipEncryption.ZipCrypto, ZipEncryption.Aes128, ZipEncryption.Aes192, ZipEncryption.Aes256 })
            {
                data.Add(method, encryption, true);
                data.Add(method, encryption, false);
            }
        }

        return data;
    }

    internal static ZipCompressionMethod[] WritableMethods =>
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

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void RoundTrip(ZipCompressionMethod method, ZipEncryption encryption, bool seekable)
    {
        var password = encryption is ZipEncryption.None ? null : Password;
        var options = new ZipCompressionOptions { CompressionMethod = method, Password = password, Encryption = encryption };
        var files = CreateFiles();
        var data = Write(files, options, seekable);

        var readerOptions = new ArchiveReaderOptions { Password = password };
        using (var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read, leaveOpen: false, readerOptions, compressionOptions: null))
        {
            Assert.Equal(files.Count + 1, archive.Entries.Count);
            var directory = archive.GetEntry("dir/")!;
            Assert.True(directory.IsDirectory);
            Assert.False(directory.IsEncrypted);
            foreach (var (name, content) in files)
            {
                var entry = archive.GetEntry(name)!;
                Assert.Equal(content.Length is 0 ? ZipCompressionMethod.Stored : method, entry.CompressionMethod);
                Assert.Equal(encryption, entry.EncryptionMethod);
                Assert.Equal(content.LongLength, entry.Length);
                Assert.Equal(content, ZipTestData.ReadAll(entry.Open()));
                Assert.Equal(!seekable || encryption is ZipEncryption.ZipCrypto, (entry.GeneralPurposeFlags & 8) != 0);
                if (encryption is ZipEncryption.Aes128 or ZipEncryption.Aes192 or ZipEncryption.Aes256)
                {
                    // AE-2 (no CRC) for the small entries, or for all of them when the stream is not seekable
                    Assert.Equal(!seekable || content.Length < 20, entry.Crc32 is null);
                }
                else
                {
                    Assert.Equal(System.IO.Hashing.Crc32.HashToUInt32(content), entry.Crc32);
                }
            }
        }

        AssertForwardReader(data, files, readerOptions);
        if (method is ZipCompressionMethod.Stored or ZipCompressionMethod.Deflate)
        {
            AssertBclReader(data, files, password);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoundTrip_Async(bool seekable)
    {
        var files = CreateFiles();
        using var output = new MemoryStream();
        Stream stream = seekable ? output : new NonSeekableWriteStream(output);
        var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, new ZipCompressionOptions { Password = Password });
        await using (archive)
        {
            foreach (var (name, content) in files)
            {
                var entryStream = archive.CreateEntry(name).Open();
                await using (entryStream)
                {
                    await entryStream.WriteAsync(content, TestContext.Current.CancellationToken);
                }
            }
        }

        AssertForwardReader(output.ToArray(), files, new ArchiveReaderOptions { Password = Password });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Zip64_Always(bool seekable)
    {
        var files = CreateFiles();
        var data = Write(files, new ZipCompressionOptions { Zip64 = Zip64Mode.Always }, seekable);

        // The zip64 end of central directory record and its locator precede the end record
        Assert.Equal(0x07064B50u, BitConverter.ToUInt32(data, data.Length - 22 - 20));
        using (var archive = new ZipArchive(new MemoryStream(data)))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.GetEntry(name)!;
                Assert.True(entry.Info.HasZip64Extra);
                Assert.Equal(content, ZipTestData.ReadAll(entry.Open()));
            }
        }

        AssertForwardReader(data, files, new ArchiveReaderOptions());
        AssertBclReader(data, files, password: null);
    }

    [Fact]
    public void Zip64_Auto_SmallArchiveHasNoZip64()
    {
        var data = Write(CreateFiles(), new ZipCompressionOptions(), seekable: true);
        Assert.Equal(0x06054B50u, BitConverter.ToUInt32(data, data.Length - 22));
        Assert.NotEqual(0x07064B50u, BitConverter.ToUInt32(data, data.Length - 22 - 20));
        using var archive = new ZipArchive(new MemoryStream(data));
        Assert.All(archive.Entries, entry => Assert.False(entry.Info.HasZip64Extra));
    }

    [Fact]
    public void Metadata_RoundTrip()
    {
        var time = new DateTimeOffset(2024, 5, 6, 7, 8, 10, TimeSpan.FromHours(2));
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.Comment = "Archive comment é";
            var file = archive.CreateEntry("café/日本.txt", new ZipEntryOptions
            {
                LastWriteTime = time,
                UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                Comment = "Entry comment ü",
            });
            using (var stream = file.Open())
            {
                stream.Write("Unicode"u8);
            }

            var dos = archive.CreateEntry("dos.txt", new ZipEntryOptions { LastWriteTime = time, Attributes = FileAttributes.ReadOnly | FileAttributes.Archive });
            dos.SetComment("set after");
            archive.CreateEntry("directory/", new ZipEntryOptions { UnixFileMode = (UnixFileMode)0x1ED });
            archive.CreateSymbolicLinkEntry("link", "café/日本.txt");

            var late = archive.CreateEntry("late.txt");
            late.SetLastWriteTime(time);
            late.SetUnixFileMode(UnixFileMode.UserExecute | UnixFileMode.UserRead);
            using (var stream = late.Open())
            {
                Assert.Throws<InvalidOperationException>(() => late.SetLastWriteTime(time));
            }

            late.SetComment("comment after the content");
        }

        output.Position = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal("Archive comment é", archive.Comment);
            var file = archive.GetEntry("café/日本.txt")!;
            Assert.Equal(time, file.LastWriteTime);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, file.UnixFileMode);
            Assert.Equal(3 << 8, file.VersionMadeBy & 0xFF00);
            Assert.Equal("Entry comment ü", file.Comment);
            Assert.Equal(0x800, file.GeneralPurposeFlags & 0x800);
            Assert.Equal("Unicode"u8.ToArray(), ZipTestData.ReadAll(file.Open()));

            var dos = archive.GetEntry("dos.txt")!;
            Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Archive, dos.Attributes);
            Assert.Null(dos.UnixFileMode);
            Assert.Equal("set after", dos.Comment);
            Assert.Equal(0, dos.Length);
            Assert.Equal(0, dos.GeneralPurposeFlags & 0x800);

            var directory = archive.GetEntry("directory/")!;
            Assert.True(directory.IsDirectory);
            Assert.Equal((UnixFileMode)0x1ED, directory.UnixFileMode);
            Assert.Equal(0x4000, (directory.ExternalAttributes >> 16) & 0xF000);

            var link = archive.GetEntry("link")!;
            Assert.Equal("café/日本.txt", link.LinkTarget);
            Assert.Equal((UnixFileMode)0x1FF, link.UnixFileMode);

            var late = archive.GetEntry("late.txt")!;
            Assert.Equal(time, late.LastWriteTime);
            Assert.Equal(UnixFileMode.UserExecute | UnixFileMode.UserRead, late.UnixFileMode);
            Assert.Equal("comment after the content", late.Comment);

            // The MS-DOS time is the local time, as ZipArchive writes it
            Assert.Equal(time.ToLocalTime().DateTime.AddTicks(-(time.ToLocalTime().DateTime.Ticks % TimeSpan.TicksPerSecond)), ZipEntryInfo.DecodeDosTime(late.Info.DosDate, late.Info.DosTime)!.Value.DateTime);
        }

        output.Position = 0;
        using (var bcl = new System.IO.Compression.ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal(time.ToLocalTime().DateTime, bcl.GetEntry("late.txt")!.LastWriteTime.DateTime);
            Assert.Equal("Unicode"u8.ToArray(), ZipTestData.ReadAll(bcl.GetEntry("café/日本.txt")!.Open()));
        }
    }

    [Fact]
    public void Times_OutOfTheMsDosRange()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("old.txt", new ZipEntryOptions { LastWriteTime = new DateTimeOffset(1970, 1, 2, 0, 0, 0, TimeSpan.Zero) });
            archive.CreateEntry("far.txt", new ZipEntryOptions { LastWriteTime = new DateTimeOffset(2200, 1, 2, 0, 0, 0, TimeSpan.Zero) });
        }

        output.Position = 0;
        using var result = new ZipArchive(output);

        // The extended timestamp has the time; the MS-DOS time is clamped
        Assert.Equal(new DateTimeOffset(1970, 1, 2, 0, 0, 0, TimeSpan.Zero), result.GetEntry("old.txt")!.LastWriteTime);
        Assert.Equal(new DateTime(1980, 1, 1), ZipEntryInfo.DecodeDosTime(result.GetEntry("old.txt")!.Info.DosDate, result.GetEntry("old.txt")!.Info.DosTime)!.Value.DateTime);
        Assert.Equal(new DateTime(2107, 12, 31, 23, 59, 58), ZipEntryInfo.DecodeDosTime(result.GetEntry("far.txt")!.Info.DosDate, result.GetEntry("far.txt")!.Info.DosTime)!.Value.DateTime);
    }

    [Fact]
    public void Entries_NotOpenedAreEmptyFiles()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(new NonSeekableWriteStream(output), ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, new ZipCompressionOptions { Password = Password }))
        {
            archive.CreateEntry("a.txt");
            archive.CreateEntry("b.txt");
        }

        using var result = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = Password }, compressionOptions: null);
        Assert.Equal(["a.txt", "b.txt"], result.Entries.Select(entry => entry.FullName));
        Assert.All(result.Entries, entry => Assert.Empty(ZipTestData.ReadAll(entry.Open())));
    }

    [Fact]
    public void Errors()
    {
        using var archive = new ZipArchive(new MemoryStream(), ZipArchiveMode.Create);
        var entry = archive.CreateEntry("a.txt");
        using (var stream = entry.Open())
        {
            Assert.Throws<InvalidOperationException>(() => archive.CreateEntry("b.txt"));
        }

        Assert.Throws<IOException>(() => entry.Open());
        Assert.Throws<NotSupportedException>(() => archive.CreateEntry("c.txt", new ZipEntryOptions { CompressionMethod = ZipCompressionMethod.Deflate64 }));
        Assert.Throws<NotSupportedException>(() => archive.CreateEntry("c.txt", new ZipEntryOptions { CompressionMethod = ZipCompressionMethod.Implode }));
        Assert.Throws<ArgumentException>(() => archive.CreateEntry("c.txt", new ZipEntryOptions { Password = "p", Encryption = ZipEncryption.Unsupported }));
        Assert.Throws<NotSupportedException>(() => entry.Delete());
        Assert.Throws<InvalidOperationException>(() => archive.CreateEntry("dir/").Open());
#if !NET11_0_OR_GREATER
        Assert.Throws<NotSupportedException>(() => archive.CreateEntry("c.txt", new ZipEntryOptions { CompressionMethod = ZipCompressionMethod.Zstandard }));
#endif

        using var read = new ZipArchive(new MemoryStream(Write(CreateFiles(), new ZipCompressionOptions(), seekable: true)));
        Assert.Throws<NotSupportedException>(() => read.CreateEntry("x"));
        Assert.Throws<NotSupportedException>(() => read.Comment = "x");
        Assert.Throws<NotSupportedException>(() => read.Entries[0].SetComment("x"));
    }

    [Fact]
    public void Update_AddDeleteReplace()
    {
        var files = CreateFiles();
        using var stream = new MemoryStream();
        stream.Write(Write(files, new ZipCompressionOptions { Password = Password, CompressionMethod = ZipCompressionMethod.Lzma }, seekable: true));
        var original = stream.ToArray();
        var originalEntry = ReadRawEntry(original, "text.txt");

        // The existing entries are copied without the password
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true, readerOptions: null, new ZipCompressionOptions { CompressionMethod = ZipCompressionMethod.BZip2 }))
        {
            archive.GetEntry("random.bin")!.Delete();
            archive.GetEntry("small.txt")!.Delete();
            using (var replaced = archive.CreateEntry("small.txt").Open())
            {
                replaced.Write("replaced"u8);
            }

            archive.CreateEntry("new/");
            archive.CreateSymbolicLinkEntry("new/link", "../text.txt");
            archive.GetEntry("text.txt")!.SetComment("updated comment");
            archive.Comment = "updated archive";
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, new ArchiveReaderOptions { Password = Password }, compressionOptions: null))
        {
            Assert.Equal(["empty.txt", "text.txt", "dir/", "dir/nested.txt", "small.txt", "new/", "new/link"], archive.Entries.Select(entry => entry.FullName));
            Assert.Equal("updated archive", archive.Comment);
            var text = archive.GetEntry("text.txt")!;
            Assert.Equal("updated comment", text.Comment);
            Assert.Equal(ZipEncryption.Aes256, text.EncryptionMethod);
            Assert.Equal(ZipCompressionMethod.Lzma, text.CompressionMethod);
            Assert.Equal(files["text.txt"], ZipTestData.ReadAll(text.Open()));
            var small = archive.GetEntry("small.txt")!;
            Assert.Equal(ZipCompressionMethod.BZip2, small.CompressionMethod);
            Assert.Equal(ZipEncryption.None, small.EncryptionMethod);
            Assert.Equal("replaced"u8.ToArray(), ZipTestData.ReadAll(small.Open()));
            Assert.Equal("../text.txt", archive.GetEntry("new/link")!.LinkTarget);
        }

        // The local header and the encrypted content are copied as they are
        Assert.Equal(originalEntry, ReadRawEntry(stream.ToArray(), "text.txt"));
        AssertForwardReader(stream.ToArray(), new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["text.txt"] = files["text.txt"], ["small.txt"] = "replaced"u8.ToArray() }, new ArchiveReaderOptions { Password = Password });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Update_CopiesDataDescriptors(bool zip64)
    {
        // Entries written to a stream that is not seekable have data descriptors, which the copy writes again
        var files = CreateFiles();
        var options = new ZipCompressionOptions { Password = Password, Encryption = ZipEncryption.ZipCrypto, Zip64 = zip64 ? Zip64Mode.Always : Zip64Mode.Auto };
        using var stream = new MemoryStream();
        stream.Write(Write(files, options, seekable: false));
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("random.bin")!.Delete();
        }

        files.Remove("random.bin");
        var data = stream.ToArray();
        using (var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = Password }, compressionOptions: null))
        {
            foreach (var (name, content) in files)
            {
                Assert.Equal(content, ZipTestData.ReadAll(archive.GetEntry(name)!.Open()));
            }
        }

        AssertForwardReader(data, files, new ArchiveReaderOptions { Password = Password });
    }

    [Fact]
    public void Update_NoChange_LeavesTheStreamUntouched()
    {
        var data = Write(CreateFiles(), new ZipCompressionOptions(), seekable: true);
        using var stream = new WriteCountingStream(data);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            Assert.Equal(ZipCompressionMethod.Deflate, archive.Entries[1].CompressionMethod);
            Assert.NotEmpty(ZipTestData.ReadAll(archive.GetEntry("text.txt")!.Open()));
        }

        Assert.Equal(0, stream.WriteCount);
        Assert.Equal(data, stream.ToArray());
    }

    [Fact]
    public void Update_EmptyStream_CreatesAnArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("a.txt").Open();
            entry.Write("a"u8);
        }

        stream.Position = 0;
        using var result = new ZipArchive(stream);
        Assert.Equal("a"u8.ToArray(), ZipTestData.ReadAll(result.GetEntry("a.txt")!.Open()));
    }

    [Fact]
    public void Update_KeepsThePrependedData()
    {
        var stub = Encoding.ASCII.GetBytes("MZ self-extracting stub");
        using var stream = new MemoryStream();
        stream.Write(stub);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("a.txt").Open();
            entry.Write("a"u8);
        }

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.CreateEntry("b/");
        }

        var data = stream.ToArray();
        Assert.Equal(stub, data[..stub.Length]);
        using var result = new ZipArchive(new MemoryStream(data));
        Assert.Equal(["a.txt", "b/"], result.Entries.Select(entry => entry.FullName));
        Assert.Equal("a"u8.ToArray(), ZipTestData.ReadAll(result.GetEntry("a.txt")!.Open()));
    }

    [Fact]
    public void CreateEntryFromFile_AndCreateFromDirectory()
    {
        var root = Directory.CreateTempSubdirectory("mfc-zip-").FullName;
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "sub"));
            File.WriteAllText(Path.Combine(source, "a.txt"), "file a");
            File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "file b");
            var time = new DateTime(2023, 3, 4, 5, 6, 8, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(source, "a.txt"), time);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path.Combine(source, "a.txt"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.CreateSymbolicLink(Path.Combine(source, "link"), "a.txt");
            }

            var archivePath = Path.Combine(root, "archive.zip");
            ZipFile.CreateFromDirectory(source, archivePath, new ZipCompressionOptions { CompressionMethod = ZipCompressionMethod.Ppmd }, includeBaseDirectory: true);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var file = archive.GetEntry("source/a.txt")!;
                Assert.Equal(new DateTimeOffset(time), file.LastWriteTime);
                Assert.Equal("file a"u8.ToArray(), ZipTestData.ReadAll(file.Open()));
                Assert.True(archive.GetEntry("source/sub/")!.IsDirectory);
                if (!OperatingSystem.IsWindows())
                {
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, file.UnixFileMode);
                    Assert.Equal("a.txt", archive.GetEntry("source/link")!.LinkTarget);
                }
            }

            var extracted = Path.Combine(root, "extracted");
            ZipFile.ExtractToDirectory(archivePath, extracted);
            Assert.Equal("file b", File.ReadAllText(Path.Combine(extracted, "source", "sub", "b.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_ExtractToDirectoryAsync()
    {
        var root = Directory.CreateTempSubdirectory("mfc-zip-").FullName;
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "empty"));
            await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "file a", TestContext.Current.CancellationToken);
            var archivePath = Path.Combine(root, "archive.zip");
            await ZipFile.CreateFromDirectoryAsync(source, archivePath, new ZipCompressionOptions { Password = Password, Encryption = ZipEncryption.ZipCrypto }, cancellationToken: TestContext.Current.CancellationToken);

            var extracted = Path.Combine(root, "extracted");
            await ZipFile.ExtractToDirectoryAsync(archivePath, extracted, options: new ArchiveReaderOptions { Password = Password }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("file a", await File.ReadAllTextAsync(Path.Combine(extracted, "a.txt"), TestContext.Current.CancellationToken));
            Assert.True(Directory.Exists(Path.Combine(extracted, "empty")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ZipArchive_Create()
    {
        #region ZipArchive_Create
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // One entry is written at a time; the stream does not need to be seekable
            var entry = archive.CreateEntry("docs/readme.txt", new ZipEntryOptions { CompressionMethod = ZipCompressionMethod.Lzma });
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Hello from zip");
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream))
        {
            var entry = archive.GetEntry("docs/readme.txt")!;
            Assert.Equal(ZipCompressionMethod.Lzma, entry.CompressionMethod);
            using var reader = new StreamReader(entry.Open());
            Assert.Equal("Hello from zip", reader.ReadToEnd());
        }
        #endregion
    }

    [Fact]
    public void ZipArchive_Password()
    {
        #region ZipArchive_Password
        var options = new ZipCompressionOptions
        {
            CompressionMethod = ZipCompressionMethod.BZip2,
            Password = "secret",
            Encryption = ZipEncryption.Aes256,
        };

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options))
        {
            using var writer = new StreamWriter(archive.CreateEntry("secret.txt").Open());
            writer.Write("Encrypted with WinZip AES");
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = "secret" }, compressionOptions: null))
        {
            var entry = archive.GetEntry("secret.txt")!;
            Assert.Equal(ZipEncryption.Aes256, entry.EncryptionMethod);
            using var reader = new StreamReader(entry.Open());
            Assert.Equal("Encrypted with WinZip AES", reader.ReadToEnd());
        }
        #endregion
    }

    [Fact]
    public void ZipArchive_Update()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("old.txt").Open());
                writer.Write("old");
            }

            #region ZipArchive_Update
            // The archive is rewritten when it is disposed: the entries that are kept are copied without being decoded
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                archive.GetEntry("old.txt")!.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("new.txt").Open());
                writer.Write("new");
            }

            using (var archive = ZipFile.OpenRead(path))
            {
                Assert.Equal("new.txt", Assert.Single(archive.Entries).FullName);
            }
            #endregion
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ZipFile_CreateFromDirectory()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("mfc-zip-").FullName;
        var archivePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            File.WriteAllText(Path.Combine(sourceDirectory, "file.txt"), "content");

            #region ZipFile_CreateFromDirectory
            var options = new ZipCompressionOptions
            {
                CompressionMethod = ZipCompressionMethod.Xz,
                CompressionLevel = CompressionLevel.SmallestSize,
            };

            ZipFile.CreateFromDirectory(sourceDirectory, archivePath, options);

            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Equal("file.txt", archive.Entries[0].FullName);
            #endregion
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            File.Delete(archivePath);
        }
    }

    internal static Dictionary<string, byte[]> CreateFiles()
    {
        var text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 2000).Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog {i * 31 % 97}\n")));
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["empty.txt"] = [],
            ["text.txt"] = text,
            ["random.bin"] = Ppmd7Tests.CreateRandomData(20_000, seed: 11),
            ["small.txt"] = "small"u8.ToArray(),
            ["dir/nested.txt"] = text.AsSpan(0, 1000).ToArray(),
        };
    }

    /// <summary>Writes the files and the "dir/" directory (before "dir/nested.txt").</summary>
    internal static byte[] Write(Dictionary<string, byte[]> files, ZipCompressionOptions options, bool seekable)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(seekable ? output : new NonSeekableWriteStream(output) as Stream, ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options))
        {
            foreach (var (name, content) in files)
            {
                if (name is "dir/nested.txt")
                {
                    archive.CreateEntry("dir/");
                }

                using var stream = archive.CreateEntry(name).Open();

                // Writes of several sizes
                var offset = 0;
                var size = 1;
                while (offset < content.Length)
                {
                    var length = Math.Min(size, content.Length - offset);
                    stream.Write(content, offset, length);
                    offset += length;
                    size = (size * 7) + 3;
                }
            }
        }

        return output.ToArray();
    }

    internal static void AssertForwardReader(byte[] data, Dictionary<string, byte[]> files, ArchiveReaderOptions options)
    {
        using var reader = ArchiveReader.Create(new ZipTestData.ForwardOnlyStream(data, 13), options);
        Assert.Equal(ArchiveFormat.Zip, reader.Format);
        var found = 0;
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry!;
            if (entry.IsDirectory || !files.TryGetValue(entry.FullName, out var content))
                continue;

            Assert.Equal(content, ZipTestData.ReadAll(reader.OpenEntryStream()));
            found++;
        }

        Assert.Equal(files.Count, found);
    }

    private static void AssertBclReader(byte[] data, Dictionary<string, byte[]> files, string? password)
    {
#if !NET11_0_OR_GREATER
        if (password is not null)
            return;
#endif

        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        foreach (var (name, content) in files)
        {
            var entry = archive.GetEntry(name)!;
#if NET11_0_OR_GREATER
            using var stream = password is null ? entry.Open() : entry.Open(password);
#else
            using var stream = entry.Open();
#endif
            Assert.Equal(content, ZipTestData.ReadAll(stream));
        }
    }

    /// <summary>Gets the local header and the content of an entry.</summary>
    private static byte[] ReadRawEntry(byte[] data, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(data));
        var info = archive.GetEntry(name)!.Info;
        var offset = (int)info.LocalHeaderOffset;
        var headerLength = 30 + BitConverter.ToUInt16(data, offset + 26) + BitConverter.ToUInt16(data, offset + 28);
        return data.AsSpan(offset, headerLength + (int)info.CompressedSize).ToArray();
    }

    /// <summary>A write-only stream that is not seekable, over a memory stream.</summary>
    internal sealed class NonSeekableWriteStream(Stream inner) : Stream
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
    }

    private sealed class WriteCountingStream : MemoryStream
    {
        public WriteCountingStream(byte[] data)
        {
            base.Write(data);
            Position = 0;
            WriteCount = 0;
        }

        public int WriteCount { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCount++;
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteCount++;
            base.Write(buffer);
        }

        public override void SetLength(long value)
        {
            WriteCount++;
            base.SetLength(value);
        }
    }

    [Fact]
    public void Update_CommentThatNeedsUtf8_KeepsTheCodePage437Names()
    {
        using var stream = new MemoryStream();
        using (var file = ZipTestData.OpenRead("cp437-names.zip"))
        {
            file.CopyTo(stream);
        }

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                entry.SetComment("日本");
            }
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal(["résumé/café.txt", "日本語.txt"], archive.Entries.Select(entry => entry.FullName));
            Assert.Equal(["日本", "日本"], archive.Entries.Select(entry => entry.Comment));
        }

        // The local headers have the same names: the forward-only reader and System.IO.Compression read them
        stream.Position = 0;
        using (var reader = ArchiveReader.Create(stream, leaveOpen: true))
        {
            var names = new List<string>();
            while (reader.MoveToNextEntry())
            {
                names.Add(reader.Entry!.FullName);
            }

            Assert.Equal(["résumé/café.txt", "日本語.txt"], names);
        }

        stream.Position = 0;
        using var bcl = new System.IO.Compression.ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(["résumé/café.txt", "日本語.txt"], bcl.Entries.Select(entry => entry.FullName));
    }

}
