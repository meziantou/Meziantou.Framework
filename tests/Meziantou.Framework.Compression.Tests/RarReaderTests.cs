using static Meziantou.Framework.Compression.Tests.RarArchiveTests;

namespace Meziantou.Framework.Compression.Tests;

public sealed class RarReaderTests
{
    public static TheoryData<string> ArchiveNames => Archives;

    [Theory]
    [MemberData(nameof(ArchiveNames))]
    public void NonSeekable_MatchesRandomAccess(string fileName)
    {
        var expected = GetExpected(fileName);
        using var reader = CreateReader(fileName, Password);
        var index = 0;
        while (reader.MoveToNextEntry())
        {
            var entry = Assert.IsType<RarArchiveEntry>(reader.Entry);
            Assert.Null(entry.Archive);
            var expectedEntry = expected.Entries[index++];
            AssertEntry(IsRar5(fileName) ? RarFormatVersion.Rar5 : RarFormatVersion.Rar4, expectedEntry, entry);
            if (expectedEntry.Sha256 is null || GetPassword(fileName, expectedEntry.Name) is not Password || expectedEntry.Type is "Hard link" or "File reference")
                continue;

            if (IsUnsupported(expectedEntry))
            {
                Assert.Throws<NotSupportedException>(() => ComputeSha256(entry));
                continue;
            }

            Assert.Equal(expectedEntry.Sha256, ComputeSha256(entry), $"Content of {expectedEntry.Name}");
        }

        Assert.Equal(expected.Entries.Length, index);
        Assert.False(reader.MoveToNextEntry());
    }

    [Theory]
    [MemberData(nameof(ArchiveNames))]
    public void NonSeekable_SkipEntries(string fileName)
    {
        // The entries that are not read are skipped; in a solid archive, they are decoded
        var expected = GetExpected(fileName);
        using var reader = CreateReader(fileName, Password);
        var index = 0;
        while (reader.MoveToNextEntry())
        {
            var expectedEntry = expected.Entries[index++];
            if (index % 2 is 1 || expectedEntry.Sha256 is null || IsUnsupported(expectedEntry) || GetPassword(fileName, expectedEntry.Name) is not Password || expectedEntry.Type is "Hard link" or "File reference")
                continue;

            Assert.Equal(expectedEntry.Sha256, ComputeSha256(reader.Entry!), $"Content of {expectedEntry.Name}");
        }

        Assert.Equal(expected.Entries.Length, index);
    }

    [Fact]
    public async Task NonSeekable_Async()
    {
        var expected = GetExpected("libarchive-rar5-multiarchive-solid.part01.rar");
        await using var reader = CreateReader("libarchive-rar5-multiarchive-solid.part01.rar", password: null);
        var names = new List<string>();
        while (await reader.MoveToNextEntryAsync())
        {
            names.Add(reader.Entry!.FullName);
        }

        Assert.Equal(expected.Entries.Select(entry => entry.Name), names);
    }

    [Fact]
    public void Entry_AfterMove_Throws()
    {
        using var reader = CreateReader("libarchive-rar5-multiple-files-solid.rar", password: null);
        Assert.True(reader.MoveToNextEntry());
        var first = reader.Entry!;
        Assert.True(reader.MoveToNextEntry());
        Assert.Throws<InvalidOperationException>(() => first.Open());
    }

    [Fact]
    public void Entry_OpenTwice_Throws()
    {
        using var reader = CreateReader("libarchive-rar5-multiple-files-solid.rar", password: null);
        Assert.True(reader.MoveToNextEntry());
        using var stream = reader.OpenEntryStream();
        Assert.Throws<InvalidOperationException>(() => reader.OpenEntryStream());
    }

    [Fact]
    public void MissingVolume_Throws()
    {
        var bytes = File.ReadAllBytes(GetPath("libarchive-rar5-multiarchive-solid.part01.rar"));
        using var reader = RarArchive.CreateForwardReader(new NonSeekableStream(bytes), options: null, leaveOpen: false);
        Assert.Throws<InvalidDataException>(() =>
        {
            while (reader.MoveToNextEntry())
            {
                ComputeSha256(reader.Entry!);
            }
        });
    }

    [Fact]
    public void WrongPassword_Throws()
    {
        using var reader = CreateReader("libarchive-rar4-solid-encrypted.rar", "wrong");
        Assert.True(reader.MoveToNextEntry());
        Assert.Throws<ArchivePasswordException>(() => ComputeSha256(reader.Entry!));
    }

    [Fact]
    public void EncryptedHeaders_WrongPassword_Throws()
    {
        using var reader = CreateReader("rarfile-rar5-hpsw.rar", "wrong");
        Assert.Throws<ArchivePasswordException>(() => reader.MoveToNextEntry());
    }

    [Fact]
    public void ExtractToDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using (var reader = CreateReader("rarfile-rar5-subdirs.rar", password: null))
            {
                reader.ExtractToDirectory(directory.FullName);
            }

            foreach (var entry in GetExpected("rarfile-rar5-subdirs.rar").Entries)
            {
                var path = Path.Combine(directory.FullName, entry.Name);
                if (entry.Type is "Directory")
                {
                    Assert.True(Directory.Exists(path));
                }
                else
                {
                    Assert.Equal(entry.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
                }
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static bool IsRar5(string fileName)
    {
        using var stream = File.OpenRead(GetPath(fileName));
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);
        return signature[6] is 1;
    }

    internal static ArchiveReader CreateReader(string fileName, string? password)
    {
        var volumes = GetVolumePaths(fileName);
        var options = new ArchiveReaderOptions
        {
            Password = password,
            VolumeResolver = index => index < volumes.Count ? new NonSeekableStream(File.ReadAllBytes(volumes[index])) : null,
        };

        return RarArchive.CreateForwardReader(new NonSeekableStream(File.ReadAllBytes(volumes[0])), options, leaveOpen: false);
    }

    internal static List<string> GetVolumePaths(string fileName)
    {
        var result = new List<string>();
        var path = GetPath(fileName);
        while (File.Exists(path))
        {
            result.Add(path);
            path = RarFile.GetVolumePath(GetPath(fileName), result.Count);
            if (!fileName.Contains(".part", StringComparison.Ordinal))
                break;
        }

        return result;
    }
}
