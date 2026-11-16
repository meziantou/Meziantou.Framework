using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Compression.Tests;

// The archives of TestData/SevenZip are made by 7-Zip 26.06 (7zz); TestData/SevenZip/README.md gives the command of each.
public sealed class SevenZipArchiveTests
{
    private const string Password = "password";

    internal static readonly byte[] HelloContent = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Hello, 7z!\n", 20)));
    internal static readonly byte[] DataContent = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 60).Select(i => $"line {i} of the data file\n")));
    internal static readonly byte[] UnicodeContent = Encoding.UTF8.GetBytes("Unicode content: é日本\n");
    internal static readonly byte[] ExecutableContent = CreateExecutableContent();
    internal const string UnicodeName = "Unicodé-日本.txt";

    // 2023-11-14T22:13:20Z, the time of the files of the fixtures
    private static readonly DateTimeOffset FixtureTime = DateTimeOffset.FromUnixTimeSeconds(1700000000);

    internal static string GetPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "SevenZip", name);

    private static byte[] CreateExecutableContent()
    {
        var result = new List<byte>();
        for (var i = 0; i < 600; i++)
        {
            result.AddRange([0x55, 0x48, 0x89, 0xE5, 0xE8]);
            var offset = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(offset, i * 37);
            result.AddRange(offset);
            result.AddRange([0x5D, 0xC3, 0x90]);
        }

        return [.. result];
    }

    private static byte[] ReadAll(ArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static SevenZipArchive OpenFixture(string name, string? password = null, long? maxMemoryUsage = null)
    {
        var options = new ArchiveReaderOptions { Password = password };
        if (maxMemoryUsage is { } max)
        {
            options.MaxMemoryUsage = max;
        }

        return new SevenZipArchive(File.OpenRead(GetPath(name)), SevenZipArchiveMode.Read, leaveOpen: false, options, compressionOptions: null);
    }

    private static void AssertStandardContent(SevenZipArchive archive)
    {
        Assert.HasCount(6, archive.Entries);
        Assert.Equal(HelloContent, ReadAll(archive.GetEntry("hello.txt")!));
        Assert.Equal(DataContent, ReadAll(archive.GetEntry("sub/data.txt")!));
        Assert.Equal(UnicodeContent, ReadAll(archive.GetEntry(UnicodeName)!));

        var empty = archive.GetEntry("empty.txt")!;
        Assert.False(empty.IsDirectory);
        Assert.Equal(0L, empty.Length);
        Assert.Empty(ReadAll(empty));

        var emptyDirectory = archive.GetEntry("emptydir/")!;
        Assert.True(emptyDirectory.IsDirectory);
        Assert.Equal("emptydir", emptyDirectory.Name);
        Assert.True(archive.GetEntry("sub/")!.IsDirectory);

        var hello = archive.GetEntry("hello.txt")!;
        Assert.Equal((long)HelloContent.Length, hello.Length);
        Assert.Equal(System.IO.Hashing.Crc32.HashToUInt32(HelloContent), hello.Crc32);
        Assert.Equal(FixtureTime, hello.LastWriteTime);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, hello.UnixFileMode);
        Assert.Equal(FileAttributes.Archive, hello.Attributes);
        Assert.Equal(ArchiveFormat.SevenZip, archive.Format);
    }

    [Theory]
    [InlineData("copy.7z")]
    [InlineData("lzma.7z")]
    [InlineData("lzma2.7z")]
    [InlineData("ppmd.7z")]
    [InlineData("bzip2.7z")]
    [InlineData("deflate.7z")]
    [InlineData("deflate64.7z")]
    [InlineData("delta.7z")]
    [InlineData("nonsolid.7z")]
    [InlineData("headers-uncompressed.7z")]
    [InlineData("times.7z")]
    public void Read_Methods(string name)
    {
        using var archive = OpenFixture(name);
        AssertStandardContent(archive);
    }

    [Theory]
    [InlineData("filter-bcj.7z")]
    [InlineData("filter-ppc.7z")]
    [InlineData("filter-ia64.7z")]
    [InlineData("filter-arm.7z")]
    [InlineData("filter-armt.7z")]
    [InlineData("filter-sparc.7z")]
    [InlineData("filter-arm64.7z")]
    [InlineData("filter-riscv.7z")]
    [InlineData("bcj2.7z")]
    public void Read_Filters(string name)
    {
        using var archive = OpenFixture(name);
        Assert.Equal(ExecutableContent, ReadAll(archive.GetEntry("app.exe")!));
        if (archive.GetEntry("hello.txt") is { } hello)
        {
            Assert.Equal(HelloContent, ReadAll(hello));
        }
    }

    [Fact]
    public void Read_CompressedLength()
    {
        using (var archive = OpenFixture("nonsolid.7z"))
        {
            Assert.True(archive.GetEntry("hello.txt")!.CompressedLength > 0);
            Assert.Equal(0L, archive.GetEntry("empty.txt")!.CompressedLength);
        }

        using (var archive = OpenFixture("lzma2.7z"))
        {
            Assert.Equal(-1L, archive.GetEntry("hello.txt")!.CompressedLength);
        }
    }

    [Fact]
    public void Read_SolidEntriesInAnyOrder()
    {
        using var archive = OpenFixture("lzma2.7z");
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["hello.txt"] = HelloContent,
            ["sub/data.txt"] = DataContent,
            [UnicodeName] = UnicodeContent,
        };

        // In order, in reverse order (restarts the folder), then twice the same entry
        foreach (var name in expected.Keys.Concat(expected.Keys.Reverse()).Append("sub/data.txt"))
        {
            Assert.Equal(expected[name], ReadAll(archive.GetEntry(name)!));
        }
    }

    [Fact]
    public void Read_TwoEntriesOfTheSameFolderAtOnce()
    {
        using var archive = OpenFixture("lzma2.7z");
        using var hello = archive.GetEntry("hello.txt")!.Open();
        using var data = archive.GetEntry("sub/data.txt")!.Open();
        var helloBuffer = new MemoryStream();
        var dataBuffer = new MemoryStream();
        var buffer = new byte[7];
        while (true)
        {
            var read1 = hello.Read(buffer);
            helloBuffer.Write(buffer, 0, read1);
            var read2 = data.Read(buffer);
            dataBuffer.Write(buffer, 0, read2);
            if (read1 is 0 && read2 is 0)
                break;
        }

        Assert.Equal(HelloContent, helloBuffer.ToArray());
        Assert.Equal(DataContent, dataBuffer.ToArray());
    }

    [Fact]
    public void Read_Times()
    {
        using var archive = OpenFixture("times.7z");
        var hello = archive.GetEntry("hello.txt")!;
        Assert.Equal(FixtureTime, hello.LastWriteTime);
        Assert.Equal(FixtureTime, hello.LastAccessTime);
        Assert.NotNull(hello.CreationTime);
    }

    [Fact]
    public void Read_SymbolicLink()
    {
        using var archive = OpenFixture("symlink.7z");
        var link = archive.GetEntry("link")!;
        Assert.Equal("hello.txt", link.LinkTarget);
        Assert.Null(archive.GetEntry("hello.txt")!.LinkTarget);
    }

    [Fact]
    public void Read_Encrypted()
    {
        using (var archive = OpenFixture("encrypted.7z", Password))
        {
            Assert.True(archive.GetEntry("hello.txt")!.IsEncrypted);
            AssertStandardContent(archive);
        }

        using (var archive = OpenFixture("encrypted.7z"))
        {
            // The names are not encrypted: the content is
            Assert.HasCount(6, archive.Entries);
            Assert.Throws<ArchivePasswordException>(() => ReadAll(archive.GetEntry("hello.txt")!));
        }

        using (var archive = OpenFixture("encrypted.7z", "wrong"))
        {
            Assert.Throws<ArchivePasswordException>(() => ReadAll(archive.GetEntry("hello.txt")!));
        }
    }

    [Fact]
    public void Read_EncryptedHeaders()
    {
        using (var archive = OpenFixture("encrypted-headers.7z", Password))
        {
            AssertStandardContent(archive);
        }

        Assert.Throws<ArchivePasswordException>(() => OpenFixture("encrypted-headers.7z"));
        Assert.Throws<ArchivePasswordException>(() => OpenFixture("encrypted-headers.7z", "wrong"));
    }

    [Fact]
    public void Read_MultiVolume()
    {
        using (var archive = SevenZipFile.OpenRead(GetPath("volumes.7z.001")))
        {
            Assert.HasCount(2, archive.Entries);
            Assert.HasCount(2500, ReadAll(archive.GetEntry("random.bin")!));
            Assert.Equal(HelloContent, ReadAll(archive.GetEntry("hello.txt")!));
        }

        var options = new ArchiveReaderOptions
        {
            VolumeResolver = index => File.Exists(GetPath($"volumes.7z.00{index + 1}")) ? File.OpenRead(GetPath($"volumes.7z.00{index + 1}")) : null,
        };
        using (var archive = new SevenZipArchive(File.OpenRead(GetPath("volumes.7z.001")), SevenZipArchiveMode.Read, leaveOpen: false, options, compressionOptions: null))
        {
            Assert.Equal(HelloContent, ReadAll(archive.GetEntry("hello.txt")!));
        }

        // Without the other volumes, the archive is truncated
        Assert.Throws<InvalidDataException>(() => OpenFixture("volumes.7z.001"));
    }

    [Fact]
    public void Read_CorruptedCrc()
    {
        using var archive = OpenFixture("corrupt-crc.7z");
        var entry = archive.Entries.First(entry => entry.Length > 0);
        Assert.Throws<InvalidDataException>(() => ReadAll(entry));
    }

    [Fact]
    public void Read_Truncated()
    {
        Assert.Throws<InvalidDataException>(() => OpenFixture("truncated.7z"));

        var data = File.ReadAllBytes(GetPath("lzma2.7z"));
        for (var length = 0; length < data.Length; length += 7)
        {
            Assert.Throws<InvalidDataException>(() => new SevenZipArchive(new MemoryStream(data, 0, length)));
        }
    }

    [Fact]
    public void Read_NotAnArchive()
    {
        Assert.Throws<InvalidDataException>(() => new SevenZipArchive(new MemoryStream(new byte[100])));
    }

    [Fact]
    public void Read_CorruptedStartHeader()
    {
        var data = File.ReadAllBytes(GetPath("copy.7z"));
        data[20] ^= 1;
        Assert.Throws<InvalidDataException>(() => new SevenZipArchive(new MemoryStream(data)));
    }

    [Fact]
    public void Read_UnknownCoder()
    {
        var data = CreateArchive(new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Copy, CompressHeaders = false }, ("a.txt", HelloContent));

        // The Copy coder of the folder: kFolder, 1 folder, not external, 1 coder, flags (ID of 1 byte), ID 00
        var header = GetNextHeader(data);
        var index = header.AsSpan().IndexOf((ReadOnlySpan<byte>)[0x0B, 0x01, 0x00, 0x01, 0x01, 0x00]);
        Assert.True(index >= 0);
        header[index + 5] = 0x7F;
        SetNextHeader(data, header);

        using var archive = new SevenZipArchive(new MemoryStream(data));
        var exception = Assert.Throws<NotSupportedException>(() => ReadAll(archive.Entries[0]));
        Assert.Contains("0x7F", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_MaxMemoryUsage()
    {
        Assert.Throws<InvalidDataException>(() =>
        {
            using var archive = OpenFixture("lzma2.7z", maxMemoryUsage: 16);
            ReadAll(archive.GetEntry("hello.txt")!);
        });
    }

    [Fact]
    public void ExtractToDirectory_DecodesEachFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mfc-7z-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var archive = OpenFixture("lzma2.7z"))
            {
                archive.ExtractToDirectory(directory);
            }

            Assert.Equal(HelloContent, File.ReadAllBytes(Path.Combine(directory, "hello.txt")));
            Assert.Equal(DataContent, File.ReadAllBytes(Path.Combine(directory, "sub", "data.txt")));
            Assert.True(Directory.Exists(Path.Combine(directory, "emptydir")));
            Assert.Equal(FixtureTime.UtcDateTime, File.GetLastWriteTimeUtc(Path.Combine(directory, "hello.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    internal static byte[] GetNextHeader(byte[] archive)
    {
        var offset = BinaryPrimitives.ReadInt64LittleEndian(archive.AsSpan(12));
        var size = BinaryPrimitives.ReadInt64LittleEndian(archive.AsSpan(20));
        return archive.AsSpan((int)(32 + offset), (int)size).ToArray();
    }

    internal static void SetNextHeader(byte[] archive, byte[] header)
    {
        var offset = BinaryPrimitives.ReadInt64LittleEndian(archive.AsSpan(12));
        header.CopyTo(archive.AsSpan((int)(32 + offset)));
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(28), System.IO.Hashing.Crc32.HashToUInt32(header));
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(8), System.IO.Hashing.Crc32.HashToUInt32(archive.AsSpan(12, 20)));
    }

    internal static byte[] CreateArchive(SevenZipCompressionOptions options, params (string Name, byte[]? Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                if (content is not null)
                {
                    using var entryStream = entry.Open();
                    entryStream.Write(content);
                }
            }
        }

        return stream.ToArray();
    }

    public static TheoryData<SevenZipCompressionMethod, bool, bool, SevenZipFilter> RoundTripData()
    {
        var data = new TheoryData<SevenZipCompressionMethod, bool, bool, SevenZipFilter>();
        foreach (var method in Enum.GetValues<SevenZipCompressionMethod>())
        {
            foreach (var solid in new[] { true, false })
            {
                foreach (var encrypted in new[] { false, true })
                {
                    data.Add(method, solid, encrypted, SevenZipFilter.Auto);
                }
            }

            data.Add(method, false, true, SevenZipFilter.None);
        }

        foreach (var filter in Enum.GetValues<SevenZipFilter>())
        {
            data.Add(SevenZipCompressionMethod.Lzma2, true, false, filter);
            data.Add(SevenZipCompressionMethod.Copy, false, true, filter);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RoundTripData))]
    public void RoundTrip(SevenZipCompressionMethod method, bool solid, bool encrypted, SevenZipFilter filter)
    {
        var options = new SevenZipCompressionOptions
        {
            Method = method,
            Solid = solid,
            Filter = filter,
            Password = encrypted ? Password : null,
            Level = 1,
        };

        RoundTripCore(options, encrypted ? Password : null);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RoundTrip_Headers(bool compressHeaders, bool encryptHeaders)
    {
        var options = new SevenZipCompressionOptions
        {
            CompressHeaders = compressHeaders,
            EncryptHeaders = encryptHeaders,
            Password = encryptHeaders ? Password : null,
            Method = SevenZipCompressionMethod.Deflate,
        };

        var data = RoundTripCore(options, options.Password);
        if (encryptHeaders)
        {
            Assert.Throws<ArchivePasswordException>(() => new SevenZipArchive(new MemoryStream(data)));
            Assert.True(data.AsSpan().IndexOf(Encoding.Unicode.GetBytes("binary.bin")) < 0);
        }
    }

    [Fact]
    public void RoundTrip_SolidLimits()
    {
        var entries = Enumerable.Range(0, 10).Select(i => ($"file{i}.txt", (byte[]?)Encoding.ASCII.GetBytes(new string((char)('a' + i), 100 + i)))).ToArray();
        foreach (var options in new[]
        {
            new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate, SolidFileCount = 3 },
            new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate, SolidBlockSize = 250 },
            new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate, Solid = false },
        })
        {
            var data = CreateArchive(options, entries);
            using var archive = new SevenZipArchive(new MemoryStream(data));
            Assert.HasCount(10, archive.Entries);
            foreach (var (name, content) in entries)
            {
                Assert.Equal(content, ReadAll(archive.GetEntry(name)!));
            }

            var folders = archive.Entries.Count(entry => entry.CompressedLength > 0);
            if (!options.Solid)
            {
                Assert.Equal(10, folders);
            }
        }
    }

    private static byte[] RoundTripCore(SevenZipCompressionOptions options, string? password)
    {
#pragma warning disable CA5394 // Reproducible test data, not a secret
        var random = new Random(42);
        var binary = new byte[100_000];
        random.NextBytes(binary.AsSpan(0, 50_000));
#pragma warning restore CA5394
        var times = new DateTimeOffset(2020, 1, 2, 3, 4, 5, 678, TimeSpan.Zero).AddTicks(9);

        using var stream = new MemoryStream();
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options))
        {
            archive.CreateEntry("dir/");
            var text = archive.CreateEntry("dir/text.txt");
            text.SetLastWriteTime(times);
            text.SetCreationTime(times.AddDays(-1));
            text.SetLastAccessTime(times.AddDays(1));
            text.SetAttributes(FileAttributes.ReadOnly | FileAttributes.Archive);
            text.SetUnixFileMode(UnixFileMode.UserRead | UnixFileMode.UserExecute);
            using (var content = text.Open())
            {
                content.Write(DataContent);
            }

            archive.CreateEntry("empty.txt");
            using (var content = archive.CreateEntry(UnicodeName).Open())
            {
                content.Write(UnicodeContent);
            }

            using (var content = archive.CreateEntry("app.exe").Open())
            {
                content.Write(ExecutableContent);
            }

            using (var content = archive.CreateEntry("binary.bin").Open())
            {
                // Several writes of various sizes
                for (var i = 0; i < binary.Length; i += 7919)
                {
                    content.Write(binary.AsSpan(i, Math.Min(7919, binary.Length - i)));
                }
            }

            archive.CreateEntry("empty-dir/");
        }

        var data = stream.ToArray();
        using (var archive = new SevenZipArchive(new MemoryStream(data), SevenZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = password }, compressionOptions: null))
        {
            Assert.Equal(["dir/", "dir/text.txt", "empty.txt", UnicodeName, "app.exe", "binary.bin", "empty-dir/"], archive.Entries.Select(entry => entry.FullName).ToArray());
            var text = archive.GetEntry("dir/text.txt")!;
            Assert.Equal(DataContent, ReadAll(text));
            Assert.Equal(times, text.LastWriteTime);
            Assert.Equal(times.AddDays(-1), text.CreationTime);
            Assert.Equal(times.AddDays(1), text.LastAccessTime);
            Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Archive, text.Attributes);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserExecute, text.UnixFileMode);
            Assert.Equal(password is not null, text.IsEncrypted);
            Assert.Equal(UnicodeContent, ReadAll(archive.GetEntry(UnicodeName)!));
            Assert.Equal(ExecutableContent, ReadAll(archive.GetEntry("app.exe")!));
            Assert.Equal(binary, ReadAll(archive.GetEntry("binary.bin")!));
            Assert.Empty(ReadAll(archive.GetEntry("empty.txt")!));
            Assert.False(archive.GetEntry("empty.txt")!.IsDirectory);
            Assert.True(archive.GetEntry("dir/")!.IsDirectory);
            Assert.True(archive.GetEntry("empty-dir/")!.IsDirectory);
            Assert.Equal(FileAttributes.Directory, archive.GetEntry("empty-dir/")!.Attributes);
        }

        return data;
    }

    [Fact]
    public void Create_EmptyArchive()
    {
        var data = CreateArchive(new SevenZipCompressionOptions());
        Assert.HasCount(32, data);
        using var archive = new SevenZipArchive(new MemoryStream(data));
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public void Create_OneEntryAtATime()
    {
        using var archive = new SevenZipArchive(new MemoryStream(), SevenZipArchiveMode.Create);
        var first = archive.CreateEntry("a.txt");
        using var stream = first.Open();
        Assert.Throws<IOException>(() => first.Open());
        Assert.Throws<InvalidOperationException>(() => archive.CreateEntry("b.txt").Open());
        Assert.Throws<InvalidOperationException>(() => archive.CreateEntry("dir/").Open());
        Assert.Throws<NotSupportedException>(first.Delete);
    }

    [Fact]
    public void Create_EntriesWrittenOutOfOrder()
    {
        using var stream = new MemoryStream();
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate }))
        {
            var a = archive.CreateEntry("a.txt");
            var b = archive.CreateEntry("b.txt");
            using (var content = b.Open())
            {
                content.Write(DataContent);
            }

            using (var content = a.Open())
            {
                content.Write(HelloContent);
            }
        }

        stream.Position = 0;
        using var result = new SevenZipArchive(stream);
        Assert.Equal(HelloContent, ReadAll(result.GetEntry("a.txt")!));
        Assert.Equal(DataContent, ReadAll(result.GetEntry("b.txt")!));
    }

    [Fact]
    public void Read_ModesAreChecked()
    {
        using var archive = OpenFixture("copy.7z");
        Assert.Throws<NotSupportedException>(() => archive.CreateEntry("a.txt"));
        Assert.Throws<NotSupportedException>(archive.Entries[0].Delete);
        Assert.Throws<NotSupportedException>(() => archive.Entries[0].SetLastWriteTime(null));
        Assert.Throws<InvalidOperationException>(() => archive.GetEntry("emptydir/")!.Open());
    }

    [Fact]
    public void Update_AddDeleteReplace()
    {
        var options = new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate, Solid = false, Password = Password };
        var original = CreateArchive(options, ("a.txt", HelloContent), ("b.txt", DataContent), ("c.txt", UnicodeContent), ("dir/", null));
        var readerOptions = new ArchiveReaderOptions { Password = Password };
        int aPackSize;
        int bPackSize;
        int cPackSize;
        using (var archive = new SevenZipArchive(new MemoryStream(original), SevenZipArchiveMode.Read, leaveOpen: false, readerOptions, compressionOptions: null))
        {
            aPackSize = (int)archive.GetEntry("a.txt")!.CompressedLength;
            bPackSize = (int)archive.GetEntry("b.txt")!.CompressedLength;
            cPackSize = (int)archive.GetEntry("c.txt")!.CompressedLength;
        }

        using var stream = new MemoryStream();
        stream.Write(original);
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Update, leaveOpen: true, readerOptions, options))
        {
            Assert.Equal(HelloContent, ReadAll(archive.GetEntry("a.txt")!));
            archive.GetEntry("b.txt")!.Delete();
            Assert.Null(archive.GetEntry("b.txt"));

            // Replace: delete, then create
            archive.GetEntry("c.txt")!.SetLastWriteTime(FixtureTime);
            using (var content = archive.CreateEntry("d.txt").Open())
            {
                content.Write(DataContent);
            }
        }

        var updated = stream.ToArray();

        // The folders of a.txt and c.txt are copied without being decoded: their encrypted bytes (with their IVs) are kept
        Assert.Equal(original.AsSpan(32, aPackSize).ToArray(), updated.AsSpan(32, aPackSize).ToArray());
        Assert.Equal(original.AsSpan(32 + aPackSize + bPackSize, cPackSize).ToArray(), updated.AsSpan(32 + aPackSize, cPackSize).ToArray());

        using (var archive = new SevenZipArchive(new MemoryStream(updated), SevenZipArchiveMode.Read, leaveOpen: false, readerOptions, compressionOptions: null))
        {
            Assert.Equal(["a.txt", "c.txt", "dir/", "d.txt"], archive.Entries.Select(entry => entry.FullName).ToArray());
            Assert.Equal(HelloContent, ReadAll(archive.GetEntry("a.txt")!));
            Assert.Equal(UnicodeContent, ReadAll(archive.GetEntry("c.txt")!));
            Assert.Equal(FixtureTime, archive.GetEntry("c.txt")!.LastWriteTime);
            Assert.Equal(DataContent, ReadAll(archive.GetEntry("d.txt")!));
        }

        // Replace an entry of the updated archive
        stream.Position = 0;
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Update, leaveOpen: true, readerOptions, options))
        {
            archive.GetEntry("a.txt")!.Delete();
            using var content = archive.CreateEntry("a.txt").Open();
            content.Write(DataContent);
        }

        stream.Position = 0;
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Read, leaveOpen: true, readerOptions, compressionOptions: null))
        {
            Assert.Equal(DataContent, ReadAll(archive.GetEntry("a.txt")!));
            Assert.HasCount(4, archive.Entries);
        }
    }

    [Fact]
    public void Update_SolidFolderIsRecompressed()
    {
        var options = new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate };
        var original = CreateArchive(options, ("a.txt", HelloContent), ("b.txt", DataContent), ("c.txt", UnicodeContent));
        using var stream = new MemoryStream();
        stream.Write(original);
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Update, leaveOpen: true, readerOptions: null, options))
        {
            archive.GetEntry("b.txt")!.Delete();
            var e = archive.CreateEntry("e.txt");
            using (var content = e.Open())
            {
                content.Write(HelloContent);
            }

            // A new entry deleted before the archive is written
            e.Delete();
            using (var content = archive.CreateEntry("f.txt").Open())
            {
                content.Write(UnicodeContent);
            }
        }

        stream.Position = 0;
        using (var archive = new SevenZipArchive(stream))
        {
            Assert.Equal(["a.txt", "c.txt", "f.txt"], archive.Entries.Select(entry => entry.FullName).ToArray());
            Assert.Equal(HelloContent, ReadAll(archive.GetEntry("a.txt")!));
            Assert.Equal(UnicodeContent, ReadAll(archive.GetEntry("c.txt")!));
            Assert.Equal(UnicodeContent, ReadAll(archive.GetEntry("f.txt")!));
        }
    }

    [Fact]
    public void Update_WithoutChangesKeepsTheArchive()
    {
        var original = File.ReadAllBytes(GetPath("copy.7z"));
        using var stream = new MemoryStream();
        stream.Write(original);
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Update, leaveOpen: true))
        {
            Assert.Equal(HelloContent, ReadAll(archive.GetEntry("hello.txt")!));
        }

        Assert.Equal(original, stream.ToArray());
    }

    [Fact]
    public void Update_FixtureFromSevenZip()
    {
        using var stream = new MemoryStream();
        stream.Write(File.ReadAllBytes(GetPath("deflate.7z")));
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Update, leaveOpen: true, readerOptions: null, new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Copy }))
        {
            archive.GetEntry("empty.txt")!.Delete();
            archive.GetEntry("sub/data.txt")!.Delete();
        }

        stream.Position = 0;
        using (var archive = new SevenZipArchive(stream))
        {
            Assert.HasCount(4, archive.Entries);
            Assert.Equal(HelloContent, ReadAll(archive.GetEntry("hello.txt")!));
            Assert.Equal(UnicodeContent, ReadAll(archive.GetEntry(UnicodeName)!));
        }
    }

    [Fact]
    public void Snippet_Read()
    {
        #region SevenZipArchive_Read
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "SevenZip", "lzma2.7z");
        using (var archive = new SevenZipArchive(File.OpenRead(path)))
        {
            var entry = archive.GetEntry("hello.txt");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry.Open());
            Assert.StartsWith("Hello, 7z!", reader.ReadToEnd(), StringComparison.Ordinal);
        }
        #endregion
    }

    [Fact]
    public void Snippet_Create()
    {
        #region SevenZipArchive_Create
        using var stream = new MemoryStream();
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true))
        {
            // Each entry is compressed as it is written, into the current solid folder
            var entry = archive.CreateEntry("docs/readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Hello from 7z");
        }

        stream.Position = 0;
        using (var archive = new SevenZipArchive(stream))
        {
            Assert.Equal("docs/readme.txt", archive.Entries[0].FullName);
        }
        #endregion
    }

    [Fact]
    public void Snippet_Password()
    {
        #region SevenZipArchive_Password
        using var stream = new MemoryStream();
        var compressionOptions = new SevenZipCompressionOptions
        {
            Password = "secret",
            EncryptHeaders = true, // Also encrypt the names of the entries
        };

        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, compressionOptions))
        {
            using var writer = new StreamWriter(archive.CreateEntry("secret.txt").Open());
            writer.Write("Top secret");
        }

        stream.Position = 0;
        var readerOptions = new ArchiveReaderOptions { Password = "secret" };
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Read, leaveOpen: true, readerOptions, compressionOptions: null))
        {
            using var reader = new StreamReader(archive.Entries[0].Open());
            Assert.Equal("Top secret", reader.ReadToEnd());
        }
        #endregion
    }
}
