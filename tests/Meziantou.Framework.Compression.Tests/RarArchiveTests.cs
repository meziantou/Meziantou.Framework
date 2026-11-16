using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Meziantou.Framework.Compression.Tests;

public sealed class RarArchiveTests
{
    internal const string Password = "password";

    private static readonly Lazy<Dictionary<string, ExpectedArchive>> ExpectedArchives = new(LoadExpectations);

    public static TheoryData<string> Archives => [.. ExpectedArchives.Value.Keys.Order(StringComparer.Ordinal)];

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Rar", fileName);

    internal static ExpectedArchive GetExpected(string fileName) => ExpectedArchives.Value[fileName];

    /// <summary>Gets the password of an entry: the test files of libarchive encrypt some entries with another password.</summary>
    internal static string GetPassword(string fileName, string entryName)
    {
        return fileName is "libarchive-rar4-encrypted.rar" or "libarchive-rar5-encrypted.rar" && entryName is "d.txt" ? "password2" : Password;
    }

    /// <summary>Gets whether the content of an entry cannot be read: a compression that is not supported.</summary>
    internal static bool IsUnsupported(ExpectedEntry entry)
    {
        return entry.Compression?.Contains("(v15) -m", StringComparison.Ordinal) is true && !entry.Compression.Contains("-m0", StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Entries_MatchUnrar(string fileName)
    {
        var expected = GetExpected(fileName);
        using var archive = RarFile.OpenRead(GetPath(fileName), new ArchiveReaderOptions { Password = Password });
        Assert.Equal(expected.Entries.Select(entry => entry.Name), archive.Entries.Select(entry => entry.FullName));
        for (var i = 0; i < expected.Entries.Length; i++)
        {
            AssertEntry(archive.FormatVersion, expected.Entries[i], archive.Entries[i]);
        }

        if (expected.Comment is not null)
        {
            Assert.Equal(expected.Comment.TrimEnd(), archive.Comment?.TrimEnd());
        }
        else
        {
            Assert.Null(archive.Comment);
        }
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Content_MatchesUnrar(string fileName)
    {
        var expected = GetExpected(fileName);
        var archives = new Dictionary<string, RarArchive>(StringComparer.Ordinal);
        try
        {
            foreach (var expectedEntry in expected.Entries)
            {
                if (expectedEntry.Sha256 is null)
                    continue;

                var password = GetPassword(fileName, expectedEntry.Name);
                if (!archives.TryGetValue(password, out var archive))
                {
                    archive = RarFile.OpenRead(GetPath(fileName), new ArchiveReaderOptions { Password = password });
                    archives.Add(password, archive);
                }

                var entry = archive.GetEntry(expectedEntry.Name);
                Assert.NotNull(entry);
                if (IsUnsupported(expectedEntry))
                {
                    Assert.Throws<NotSupportedException>(() => ComputeSha256(entry));
                    continue;
                }

                Assert.Equal(expectedEntry.Sha256, ComputeSha256(entry), $"Content of {expectedEntry.Name}");
            }
        }
        finally
        {
            foreach (var archive in archives.Values)
            {
                archive.Dispose();
            }
        }
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Content_ReverseOrder_MatchesUnrar(string fileName)
    {
        // In a solid archive, reading an entry decodes the entries before it
        var expected = GetExpected(fileName);
        using var archive = RarFile.OpenRead(GetPath(fileName), new ArchiveReaderOptions { Password = Password });
        foreach (var expectedEntry in expected.Entries.Reverse())
        {
            if (expectedEntry.Sha256 is null || IsUnsupported(expectedEntry) || GetPassword(fileName, expectedEntry.Name) is not Password)
                continue;

            var entry = archive.GetEntry(expectedEntry.Name);
            Assert.NotNull(entry);
            Assert.Equal(expectedEntry.Sha256, ComputeSha256(entry), $"Content of {expectedEntry.Name}");
        }
    }

    [Fact]
    public void Constructor_NonSeekableStream_Throws()
    {
        using var stream = new NonSeekableStream(File.ReadAllBytes(GetPath("libarchive-rar.rar")));
        Assert.Throws<ArgumentException>("stream", () => new RarArchive(stream));
    }

    [Fact]
    public void Constructor_NotRar_Throws()
    {
        using var stream = new MemoryStream(new byte[100]);
        Assert.Throws<InvalidDataException>(() => new RarArchive(stream));
    }

    [Fact]
    public void Constructor_Rar14_Throws()
    {
        using var stream = new MemoryStream("RE~^\u0000\u0000\u0000\u0000"u8.ToArray());
        Assert.Throws<NotSupportedException>(() => new RarArchive(stream));
    }

    [Theory]
    [InlineData("libarchive-rar.rar")]
    [InlineData("libarchive-rar5-stored.rar")]
    [InlineData("rarfile-rar5-blake.rar")]
    public void Constructor_Sfx(string fileName)
    {
        // An SFX archive is an executable followed by the archive
        var archiveBytes = File.ReadAllBytes(GetPath(fileName));
        var stub = new byte[50_000];
        "MZ"u8.CopyTo(stub);
        for (var i = 2; i < stub.Length; i++)
        {
            stub[i] = (byte)(i * 31);
        }

        using var stream = new MemoryStream([.. stub, .. archiveBytes]);
        using var archive = new RarArchive(stream);
        var expected = GetExpected(fileName);
        Assert.Equal(expected.Entries.Select(entry => entry.Name), archive.Entries.Select(entry => entry.FullName));
        foreach (var expectedEntry in expected.Entries.Where(entry => entry.Sha256 is not null))
        {
            Assert.Equal(expectedEntry.Sha256, ComputeSha256(archive.GetEntry(expectedEntry.Name)!));
        }
    }

    [Fact]
    public void Properties_Rar5()
    {
        using var archive = RarFile.OpenRead(GetPath("rarfile-rar5-blake.rar"));
        Assert.Equal(ArchiveFormat.Rar, archive.Format);
        Assert.Equal(RarFormatVersion.Rar5, archive.FormatVersion);
        Assert.False(archive.IsSolid);
        Assert.False(archive.IsMultiVolume);
        var entry = archive.GetEntry("stest1.txt");
        Assert.NotNull(entry);
        Assert.Same(archive, entry.Archive);
        Assert.Equal(2048, entry.Length);
        Assert.Equal(55, entry.CompressedLength);
        Assert.Null(entry.Crc32);
        Assert.Equal("7cd5c1ac31f0cf58844a57fb9072c44768dbea1456e37c21e491f4853982ede0", Convert.ToHexStringLower(entry.Blake2spHash.Span));
        Assert.Equal(RarHostOperatingSystem.Unix, entry.HostOperatingSystem);
        Assert.Equal(50, entry.CompressionVersion);
        Assert.Equal(3, entry.CompressionMethod);
        Assert.Equal(128 * 1024, entry.DictionarySize);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, entry.UnixFileMode);
        Assert.Null(entry.Attributes);
        Assert.False(entry.IsEncrypted);
        Assert.False(entry.IsSolid);
        Assert.Equal("stest1.txt", entry.Name);
    }

    [Fact]
    public void Properties_Rar4()
    {
        using var archive = RarFile.OpenRead(GetPath("libarchive-rar-windows.rar"));
        Assert.Equal(RarFormatVersion.Rar4, archive.FormatVersion);
        var entry = archive.GetEntry("testdir/test.txt");
        Assert.NotNull(entry);
        Assert.Equal(RarHostOperatingSystem.Windows, entry.HostOperatingSystem);
        Assert.Equal(FileAttributes.Archive, entry.Attributes);
        Assert.Null(entry.UnixFileMode);
        Assert.Equal(20, entry.CompressionVersion);
        Assert.Equal(0, entry.CompressionMethod);
        Assert.True(entry.Blake2spHash.IsEmpty);
        Assert.NotNull(entry.Crc32);
        Assert.True(archive.GetEntry("testdir")!.IsDirectory);
    }

    [Fact]
    public void Solid()
    {
        using var archive = RarFile.OpenRead(GetPath("libarchive-rar5-multiple-files-solid.rar"));
        Assert.True(archive.IsSolid);
        Assert.False(archive.Entries[0].IsSolid);
        Assert.True(archive.Entries[1].IsSolid);
    }

    [Fact]
    public void Links()
    {
        using var archive = RarFile.OpenRead(GetPath("libarchive-rar5-symlink.rar"));
        Assert.Equal("file.txt", archive.GetEntry("symlink.txt")?.LinkTarget);

        using var hardLinks = RarFile.OpenRead(GetPath("libarchive-rar5-hardlink.rar"));
        var link = hardLinks.Entries.Single(entry => entry.HardLinkTarget is not null);
        Assert.Equal("file.txt", link.HardLinkTarget);
        Assert.Null(link.LinkTarget);

        using var rar4 = RarFile.OpenRead(GetPath("libarchive-rar.rar"));
        Assert.Equal("test.txt", rar4.GetEntry("testlink")?.LinkTarget);
    }

    [Fact]
    public void Password_RarFiveHeaders_Wrong()
    {
        Assert.Throws<ArchivePasswordException>(() => RarFile.OpenRead(GetPath("rarfile-rar5-hpsw.rar"), new ArchiveReaderOptions { Password = "wrong" }));
        Assert.Throws<ArchivePasswordException>(() => RarFile.OpenRead(GetPath("rarfile-rar5-hpsw.rar")));
    }

    [Fact]
    public void Password_RarFourHeaders_Wrong()
    {
        Assert.Throws<ArchivePasswordException>(() => RarFile.OpenRead(GetPath("rarfile-rar3-comment-hpsw.rar"), new ArchiveReaderOptions { Password = "wrong" }));
        Assert.Throws<ArchivePasswordException>(() => RarFile.OpenRead(GetPath("libarchive-rar4-encrypted-filenames.rar")));
    }

    [Fact]
    public void Password_RarFive_Wrong()
    {
        using var archive = RarFile.OpenRead(GetPath("rarfile-rar5-psw-blake.rar"), new ArchiveReaderOptions { Password = "wrong" });
        var entry = archive.Entries.First(entry => entry.IsEncrypted);
        Assert.Throws<ArchivePasswordException>(() => entry.Open());
    }

    [Fact]
    public void Password_RarFour_Wrong()
    {
        using var archive = RarFile.OpenRead(GetPath("libarchive-rar4-encrypted.rar"), new ArchiveReaderOptions { Password = "wrong" });
        var entry = archive.GetEntry("b.txt");
        Assert.NotNull(entry);
        Assert.True(entry.IsEncrypted);
        Assert.Throws<ArchivePasswordException>(() => ComputeSha256(entry));

        // The entries that are not encrypted do not need the password
        Assert.False(archive.GetEntry("a.txt")!.IsEncrypted);
        Assert.Equal(GetExpected("libarchive-rar4-encrypted.rar").Entries.Single(e => e.Name is "a.txt").Sha256, ComputeSha256(archive.GetEntry("a.txt")!));
    }

    [Fact]
    public void Password_Missing()
    {
        using var archive = RarFile.OpenRead(GetPath("libarchive-rar4-encrypted.rar"));
        Assert.Throws<ArchivePasswordException>(() => archive.GetEntry("b.txt")!.Open());
    }

    [Fact]
    public void Password_Example()
    {
        var path = GetPath("rarfile-rar5-hpsw.rar");

        #region RarArchive_Password
        using var archive = RarFile.OpenRead(path, new ArchiveReaderOptions { Password = "password" });
        foreach (var entry in archive.Entries)
        {
            Assert.True(entry.IsEncrypted);
            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd();
            Assert.HasCount((int)entry.Length, content);
        }
        #endregion
    }

    [Fact]
    public void Read_Example()
    {
        var path = GetPath("libarchive-rar5-multiple-files-solid.rar");

        #region RarArchive_Read
        using var stream = File.OpenRead(path);
        using var archive = new RarArchive(stream);
        Assert.Equal(RarFormatVersion.Rar5, archive.FormatVersion);
        Assert.True(archive.IsSolid);
        foreach (var entry in archive.Entries)
        {
            using var content = entry.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            Assert.Equal(entry.Length, buffer.Length);
        }
        #endregion
    }

    [Fact]
    public void Corrupted_CompressedData()
    {
        var bytes = File.ReadAllBytes(GetPath("libarchive-rar5-compressed.rar"));
        using (var archive = new RarArchive(new MemoryStream(bytes)))
        {
            var entry = archive.Entries[0];
            var offset = FindDataOffset(bytes, archive.Entries[0]);
            bytes[offset + 20] ^= 0x55;
        }

        using var corrupted = new RarArchive(new MemoryStream(bytes));
        Assert.Throws<InvalidDataException>(() => ComputeSha256(corrupted.Entries[0]));
    }

    [Fact]
    public void Corrupted_StoredData()
    {
        var bytes = File.ReadAllBytes(GetPath("libarchive-rar5-stored.rar"));
        bytes[^20] ^= 0x55;
        using var archive = new RarArchive(new MemoryStream(bytes));
        Assert.Throws<InvalidDataException>(() => ComputeSha256(archive.Entries[0]));
    }

    [Theory]
    [InlineData("libarchive-rar.rar")]
    [InlineData("rarfile-rar5-times.rar")]
    public void Corrupted_Header(string fileName)
    {
        var bytes = File.ReadAllBytes(GetPath(fileName));

        // A byte of the header of the first entry
        bytes[40] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => new RarArchive(new MemoryStream(bytes)));
    }

    [Fact]
    public void Truncated()
    {
        var bytes = File.ReadAllBytes(GetPath("libarchive-rar-compress-normal.rar"));
        using var archive = new RarArchive(new MemoryStream(bytes[..^5000]));
        Assert.Throws<InvalidDataException>(() =>
        {
            foreach (var entry in archive.Entries)
            {
                ComputeSha256(entry);
            }
        });
    }

    [Fact]
    public void MaxMemoryUsage_TooSmall()
    {
        using var archive = RarFile.OpenRead(GetPath("libarchive-rar5-compressed.rar"), new ArchiveReaderOptions { MaxMemoryUsage = 1024 });
        Assert.Throws<InvalidDataException>(() => archive.Entries[0].Open());
    }

    [Fact]
    public void ExtractToDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using (var archive = RarFile.OpenRead(GetPath("libarchive-rar5-multiple-files-solid.rar")))
            {
                archive.ExtractToDirectory(directory.FullName);
            }

            var expected = GetExpected("libarchive-rar5-multiple-files-solid.rar");
            foreach (var entry in expected.Entries)
            {
                Assert.Equal(entry.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory.FullName, entry.Name)))));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    internal static string ComputeSha256(ArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[7919];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static void AssertEntry(RarFormatVersion format, ExpectedEntry expected, RarArchiveEntry actual)
    {
        var message = expected.Name;
        Assert.Equal(expected.Name, actual.FullName);
        Assert.Equal(expected.Type is "Directory", actual.IsDirectory, message);
        switch (expected.Type)
        {
            case "Unix symbolic link" or "Windows symbolic link" or "NTFS junction point":
                Assert.Equal(expected.Target, actual.LinkTarget, message);
                break;

            case "Hard link" or "File reference":
                Assert.Equal(expected.Target, actual.HardLinkTarget, message);
                break;

            default:
                if (!actual.IsDirectory)
                {
                    Assert.Null(actual.HardLinkTarget, message);
                }

                break;
        }

        if (!actual.IsDirectory)
        {
            Assert.Equal(expected.Size, actual.Length, message);
        }

        AssertTime(format, expected.MTime, actual.LastWriteTime, message);
        AssertTime(format, expected.CTime, actual.CreationTime, message);
        AssertTime(format, expected.ATime, actual.LastAccessTime, message);
        Assert.Equal(expected.HostOS, actual.HostOperatingSystem is RarHostOperatingSystem.MSDos ? "DOS" : actual.HostOperatingSystem.ToString(), message);
        Assert.Equal(expected.Attributes, FormatAttributes(actual), message);
        if (expected.Blake2 is not null && !actual.Blake2spHash.IsEmpty)
        {
            Assert.Equal(expected.Blake2, Convert.ToHexStringLower(actual.Blake2spHash.Span), message);
        }

        if (expected.Crc32 is not null && actual.Crc32 is { } crc && expected.Type is "File")
        {
            Assert.Equal(expected.Crc32, crc.ToString("X8", CultureInfo.InvariantCulture), message);
        }

        if (expected.Compression is not null)
        {
            // RAR 1.5(v29) -m3 -md=128k
            var version = expected.Compression[(expected.Compression.IndexOf("(v", StringComparison.Ordinal) + 2)..];
            Assert.Equal(int.Parse(version[..version.IndexOf(')', StringComparison.Ordinal)], CultureInfo.InvariantCulture), actual.CompressionVersion, message);
            var method = expected.Compression[(expected.Compression.IndexOf(" -m", StringComparison.Ordinal) + 3)..];
            Assert.Equal(method[0] - '0', actual.CompressionMethod, message);
            var dictionaryIndex = expected.Compression.IndexOf("-md=", StringComparison.Ordinal);
            if (dictionaryIndex >= 0 && !actual.IsDirectory)
            {
                var value = expected.Compression[(dictionaryIndex + 4)..];
                var size = long.Parse(value[..^1], CultureInfo.InvariantCulture) * (value[^1] switch { 'k' => 1024L, 'm' => 1024L * 1024, 'g' => 1024L * 1024 * 1024, _ => 1 });
                Assert.Equal(size, actual.DictionarySize, message);
            }
        }
    }

    private static void AssertTime(RarFormatVersion format, string? expected, DateTimeOffset? actual, string message)
    {
        // unrar shows the DOS time 0 (an invalid date) as 1979-11-30
        if (expected is null || (format is RarFormatVersion.Rar4 && expected.StartsWith("1979-11-30 00:00:00", StringComparison.Ordinal)))
        {
            Assert.Null(actual, message);
            return;
        }

        Assert.NotNull(actual, message);

        // unrar shows the times of RAR 1.5-4.x as they are stored (local times), and the times of RAR5 in UTC (TZ=UTC)
        var time = format is RarFormatVersion.Rar4 ? actual.Value.DateTime : actual.Value.UtcDateTime;
        Assert.Equal(expected[..27], time.ToString("yyyy-MM-dd HH:mm:ss,fffffff", CultureInfo.InvariantCulture), message);
    }

    private static string? FormatAttributes(RarArchiveEntry entry)
    {
        var value = entry.HostAttributes;
        if (entry.HostOperatingSystem is RarHostOperatingSystem.Unix)
        {
            var type = (value & 0xF000) switch
            {
                0x4000 => 'd',
                0xA000 => 'l',
                _ => '-',
            };

            Span<char> chars = stackalloc char[10];
            chars[0] = type;
            const string Rwx = "rwxrwxrwx";
            for (var i = 0; i < 9; i++)
            {
                chars[i + 1] = (value & (1u << (8 - i))) != 0 ? Rwx[i] : '-';
            }

            return chars.ToString();
        }

        return string.Create(7, value, static (span, value) =>
        {
            span[0] = (value & 0x2000) != 0 ? 'I' : '.';
            span[1] = (value & 0x0800) != 0 ? 'C' : '.';
            span[2] = (value & 0x0020) != 0 ? 'A' : '.';
            span[3] = (value & 0x0010) != 0 ? 'D' : '.';
            span[4] = (value & 0x0004) != 0 ? 'S' : '.';
            span[5] = (value & 0x0002) != 0 ? 'H' : '.';
            span[6] = (value & 0x0001) != 0 ? 'R' : '.';
        });
    }

    private static int FindDataOffset(byte[] archive, RarArchiveEntry entry)
    {
        // The compressed data are at the end of the archive, before the end header
        return archive.Length - (int)entry.CompressedLength - 8;
    }

    private static Dictionary<string, ExpectedArchive> LoadExpectations()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(GetPath("expected.json")));
        var result = new Dictionary<string, ExpectedArchive>(StringComparer.Ordinal);
        foreach (var archive in document.RootElement.EnumerateObject())
        {
            var entries = archive.Value.GetProperty("entries").EnumerateArray().Select(entry => new ExpectedEntry(
                entry.GetProperty("name").GetString()!,
                entry.GetProperty("type").GetString()!,
                entry.GetProperty("size").GetInt64(),
                entry.GetProperty("mtime").GetString(),
                entry.GetProperty("ctime").GetString(),
                entry.GetProperty("atime").GetString(),
                entry.GetProperty("attributes").GetString(),
                entry.GetProperty("crc32").GetString(),
                entry.GetProperty("blake2").GetString(),
                entry.GetProperty("hostOS").GetString(),
                entry.GetProperty("compression").GetString(),
                entry.GetProperty("target").GetString(),
                entry.TryGetProperty("sha256", out var sha) ? sha.GetString() : null)).ToArray();
            result.Add(archive.Name, new ExpectedArchive(archive.Value.GetProperty("comment").GetString(), entries));
        }

        return result;
    }

    internal sealed record ExpectedArchive(string? Comment, ExpectedEntry[] Entries);

    internal sealed record ExpectedEntry(string Name, string Type, long Size, string? MTime, string? CTime, string? ATime, string? Attributes, string? Crc32, string? Blake2, string? HostOS, string? Compression, string? Target, string? Sha256);

    /// <summary>A stream that cannot seek, like a network stream, and whose reads return few bytes.</summary>
    internal sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 1000));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
