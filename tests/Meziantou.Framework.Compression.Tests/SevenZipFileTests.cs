namespace Meziantou.Framework.Compression.Tests;

public sealed class SevenZipFileTests
{
    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mfc-7z-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string CreateSourceDirectory()
    {
        var source = CreateTemporaryDirectory();
        File.WriteAllBytes(Path.Combine(source, "hello.txt"), SevenZipArchiveTests.HelloContent);
        Directory.CreateDirectory(Path.Combine(source, "sub", "nested"));
        File.WriteAllBytes(Path.Combine(source, "sub", "data.txt"), SevenZipArchiveTests.DataContent);
        File.WriteAllBytes(Path.Combine(source, "sub", "app.exe"), SevenZipArchiveTests.ExecutableContent);
        File.WriteAllBytes(Path.Combine(source, SevenZipArchiveTests.UnicodeName), SevenZipArchiveTests.UnicodeContent);
        File.WriteAllBytes(Path.Combine(source, "empty.txt"), []);
        Directory.CreateDirectory(Path.Combine(source, "emptydir"));
        File.SetLastWriteTimeUtc(Path.Combine(source, "hello.txt"), new DateTime(2021, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(source, "link"), "hello.txt");
            File.SetUnixFileMode(Path.Combine(source, "sub", "app.exe"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return source;
    }

    private static void AssertExtracted(string directory)
    {
        Assert.Equal(SevenZipArchiveTests.HelloContent, File.ReadAllBytes(Path.Combine(directory, "hello.txt")));
        Assert.Equal(SevenZipArchiveTests.DataContent, File.ReadAllBytes(Path.Combine(directory, "sub", "data.txt")));
        Assert.Equal(SevenZipArchiveTests.ExecutableContent, File.ReadAllBytes(Path.Combine(directory, "sub", "app.exe")));
        Assert.Equal(SevenZipArchiveTests.UnicodeContent, File.ReadAllBytes(Path.Combine(directory, SevenZipArchiveTests.UnicodeName)));
        Assert.Empty(File.ReadAllBytes(Path.Combine(directory, "empty.txt")));
        Assert.True(Directory.Exists(Path.Combine(directory, "emptydir")));
        Assert.True(Directory.Exists(Path.Combine(directory, "sub", "nested")));
        Assert.Equal(new DateTime(2021, 5, 6, 7, 8, 9, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(directory, "hello.txt")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("hello.txt", new FileInfo(Path.Combine(directory, "link")).LinkTarget);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(directory, "sub", "app.exe")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateFromDirectory_ExtractToDirectory(bool includeBaseDirectory)
    {
        var source = CreateSourceDirectory();
        var destination = CreateTemporaryDirectory();
        var archivePath = Path.Combine(destination, "archive.7z");
        try
        {
            SevenZipFile.CreateFromDirectory(source, archivePath, new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate }, includeBaseDirectory);
            var output = Path.Combine(destination, "output");
            SevenZipFile.ExtractToDirectory(archivePath, output);
            AssertExtracted(includeBaseDirectory ? Path.Combine(output, Path.GetFileName(source)) : output);

            Assert.Throws<IOException>(() => SevenZipFile.ExtractToDirectory(archivePath, output));
            SevenZipFile.ExtractToDirectory(archivePath, output, overwriteFiles: true);
        }
        finally
        {
            DeleteDirectory(source);
            DeleteDirectory(destination);
        }
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_ExtractToDirectoryAsync()
    {
        var source = CreateSourceDirectory();
        var destination = CreateTemporaryDirectory();
        var archivePath = Path.Combine(destination, "archive.7z");
        try
        {
            await SevenZipFile.CreateFromDirectoryAsync(source, archivePath, new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Copy, Password = "pass" }, cancellationToken: TestContext.Current.CancellationToken);
            var output = Path.Combine(destination, "output");
            await SevenZipFile.ExtractToDirectoryAsync(archivePath, output, options: new ArchiveReaderOptions { Password = "pass" }, cancellationToken: TestContext.Current.CancellationToken);
            AssertExtracted(output);
        }
        finally
        {
            DeleteDirectory(source);
            DeleteDirectory(destination);
        }
    }

    [Fact]
    public void Open_Update_CreatesTheArchive()
    {
        var destination = CreateTemporaryDirectory();
        var archivePath = Path.Combine(destination, "archive.7z");
        try
        {
            var options = new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Deflate };
            using (var archive = SevenZipFile.Open(archivePath, SevenZipArchiveMode.Update, compressionOptions: options))
            {
                Assert.Empty(archive.Entries);
                using var stream = archive.CreateEntry("a.txt").Open();
                stream.Write(SevenZipArchiveTests.HelloContent);
            }

            using (var archive = SevenZipFile.Open(archivePath, SevenZipArchiveMode.Update, compressionOptions: options))
            {
                using var stream = archive.CreateEntry("b.txt").Open();
                stream.Write(SevenZipArchiveTests.DataContent);
            }

            using (var archive = SevenZipFile.OpenRead(archivePath))
            {
                Assert.Equal(["a.txt", "b.txt"], archive.Entries.Select(entry => entry.FullName).ToArray());
            }
        }
        finally
        {
            DeleteDirectory(destination);
        }
    }

    [Fact]
    public void ExtractToDirectory_MultiVolume()
    {
        var destination = CreateTemporaryDirectory();
        try
        {
            SevenZipFile.ExtractToDirectory(SevenZipArchiveTests.GetPath("volumes.7z.001"), destination);
            Assert.Equal(2500L, new FileInfo(Path.Combine(destination, "random.bin")).Length);
        }
        finally
        {
            DeleteDirectory(destination);
        }
    }

    [Fact]
    public void ExtractToDirectory_RejectsEntriesOutsideOfTheDirectory()
    {
        var data = SevenZipArchiveTests.CreateArchive(new SevenZipCompressionOptions { Method = SevenZipCompressionMethod.Copy }, ("../evil.txt", SevenZipArchiveTests.HelloContent));
        var destination = CreateTemporaryDirectory();
        try
        {
            using var archive = new SevenZipArchive(new MemoryStream(data));
            Assert.Throws<IOException>(() => archive.ExtractToDirectory(Path.Combine(destination, "output")));
            Assert.False(File.Exists(Path.Combine(destination, "evil.txt")));
        }
        finally
        {
            DeleteDirectory(destination);
        }
    }

    [Fact]
    public void Snippet_CreateFromDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(Path.Combine(sourceDirectory, "file.txt"), "content");
            var archivePath = Path.Combine(root, "archive.7z");

            #region SevenZipFile_CreateFromDirectory
            var options = new SevenZipCompressionOptions
            {
                Method = SevenZipCompressionMethod.Lzma2,
                Level = 9,
            };

            SevenZipFile.CreateFromDirectory(sourceDirectory, archivePath, options);

            using var archive = SevenZipFile.OpenRead(archivePath);
            Assert.Equal("file.txt", archive.Entries[0].FullName);
            #endregion
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Snippet_ExtractToDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = SevenZipArchiveTests.GetPath("lzma2.7z");
            var destinationDirectory = Path.Combine(root, "output");

            #region SevenZipFile_ExtractToDirectory
            SevenZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);

            Assert.True(File.Exists(Path.Combine(destinationDirectory, "hello.txt")));
            #endregion
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
}
