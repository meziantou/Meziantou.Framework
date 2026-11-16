using System.Security.Cryptography;
using static Meziantou.Framework.Compression.Tests.RarArchiveTests;

namespace Meziantou.Framework.Compression.Tests;

public sealed class RarFileTests
{
    [Theory]
    [InlineData("a.part1.rar", 1, "a.part2.rar")]
    [InlineData("a.part1.rar", 9, "a.part10.rar")]
    [InlineData("a.part01.rar", 1, "a.part02.rar")]
    [InlineData("a.part0001.rar", 3, "a.part0004.rar")]
    [InlineData("a.part9.rar", 1, "a.part10.rar")]
    [InlineData("a.rar", 1, "a.r00")]
    [InlineData("a.rar", 2, "a.r01")]
    [InlineData("a.rar", 101, "a.s00")]
    [InlineData("A.RAR", 1, "A.R00")]
    [InlineData("a.b.rar", 1, "a.b.r00")]
    public void GetVolumePath(string fileName, int index, string expected)
    {
        var directory = Path.GetTempPath();
        Assert.Equal(Path.Join(directory, expected), RarFile.GetVolumePath(Path.Join(directory, fileName), index));
    }

    [Fact]
    public void OpenRead_NewVolumeNaming()
    {
        var expected = GetExpected("libarchive-rar5-multiarchive-solid.part01.rar");
        using var archive = RarFile.OpenRead(GetPath("libarchive-rar5-multiarchive-solid.part01.rar"));
        Assert.True(archive.IsMultiVolume);
        Assert.True(archive.IsSolid);
        Assert.Equal(4, archive.VolumeCount);
        AssertContent(expected, archive);
    }

    [Fact]
    public void OpenRead_OldVolumeNaming()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string[] names = ["archive.rar", "archive.r00", "archive.r01", "archive.r02"];
            for (var i = 0; i < names.Length; i++)
            {
                File.Copy(GetPath($"libarchive-rar-multivolume.part000{i + 1}.rar"), Path.Combine(directory.FullName, names[i]));
            }

            var expected = GetExpected("libarchive-rar-multivolume.part0001.rar");
            using var archive = RarFile.OpenRead(Path.Combine(directory.FullName, "archive.rar"));
            Assert.Equal(4, archive.VolumeCount);
            Assert.Equal(expected.Entries.Select(entry => entry.Name), archive.Entries.Select(entry => entry.FullName));
            AssertContent(expected, archive);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void OpenRead_MissingVolume()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "archive.part01.rar");
            File.Copy(GetPath("libarchive-rar5-multiarchive-solid.part01.rar"), path);
            using var archive = RarFile.OpenRead(path);
            Assert.Equal(1, archive.VolumeCount);

            // The last entry of the volume continues in the missing volume
            Assert.Throws<InvalidDataException>(() => archive.Entries[^1].Open());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void VolumeResolver()
    {
        var volumes = RarReaderTests.GetVolumePaths("libarchive-rar-multivolume.part0001.rar");
        Assert.HasCount(4, volumes);
        var resolved = new List<int>();
        var options = new ArchiveReaderOptions
        {
            VolumeResolver = index =>
            {
                resolved.Add(index);
                return index < volumes.Count ? new MemoryStream(File.ReadAllBytes(volumes[index])) : null;
            },
        };

        using var archive = new RarArchive(new MemoryStream(File.ReadAllBytes(volumes[0])), options);
        Assert.Equal([1, 2, 3], resolved);
        AssertContent(GetExpected("libarchive-rar-multivolume.part0001.rar"), archive);
    }

    [Fact]
    public void ExtractToDirectory_Example()
    {
        var archivePath = GetPath("rarfile-rar5-subdirs.rar");
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var destination = directory.FullName;

            #region RarFile_ExtractToDirectory
            RarFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
            Assert.True(File.Exists(Path.Combine(destination, "sub/dir1/file1.txt")));
            #endregion
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_MultiVolume()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await RarFile.ExtractToDirectoryAsync(GetPath("libarchive-rar5-multiarchive-solid.part01.rar"), directory.FullName);
            foreach (var entry in GetExpected("libarchive-rar5-multiarchive-solid.part01.rar").Entries)
            {
                Assert.Equal(entry.Sha256, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory.FullName, entry.Name)))));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExtractToDirectory_Exists_Throws()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            RarFile.ExtractToDirectory(GetPath("libarchive-rar5-stored.rar"), directory.FullName);
            Assert.Throws<IOException>(() => RarFile.ExtractToDirectory(GetPath("libarchive-rar5-stored.rar"), directory.FullName));
            RarFile.ExtractToDirectory(GetPath("libarchive-rar5-stored.rar"), directory.FullName, overwriteFiles: true);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExtractToDirectory_Encrypted()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            RarFile.ExtractToDirectory(GetPath("libarchive-rar5-solid-encrypted-filenames.rar"), directory.FullName, options: new ArchiveReaderOptions { Password = Password });
            foreach (var entry in GetExpected("libarchive-rar5-solid-encrypted-filenames.rar").Entries)
            {
                Assert.Equal(entry.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory.FullName, entry.Name)))));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void AssertContent(ExpectedArchive expected, RarArchive archive)
    {
        foreach (var expectedEntry in expected.Entries)
        {
            if (expectedEntry.Sha256 is null)
                continue;

            Assert.Equal(expectedEntry.Sha256, ComputeSha256(archive.GetEntry(expectedEntry.Name)!), expectedEntry.Name);
        }
    }
}
