namespace Meziantou.Framework.Tests;

public class TemporaryDirectoryTests
{
    [Fact]
    public void CreateInParallel()
    {
        const int Iterations = 400;
        var dirs = new TemporaryDirectory[Iterations];

        try
        {
            Parallel.For(0, Iterations, new ParallelOptions { MaxDegreeOfParallelism = 50 }, i =>
            {
                dirs[i] = TemporaryDirectory.Create();
                dirs[i].CreateEmptyFile("test.txt");
            });

            Assert.HasCount(Iterations, dirs.Select(dir => dir.FullPath).Distinct());

            Assert.All(dirs, dir => Assert.True(Directory.Exists(dir.FullPath)));
        }
        finally
        {
            foreach (var item in dirs)
            {
                item?.Dispose();
            }
        }
    }

    [Fact]
    public void DisposedDeletedDirectory()
    {
        FullPath path;
        using (var dir = TemporaryDirectory.Create())
        {
            path = dir.FullPath;
            File.WriteAllText(dir.GetFullPath("a.txt"), "content");
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task DisposeAsyncDeletedDirectory()
    {
        FullPath path;
        await using (var dir = TemporaryDirectory.Create())
        {
            path = dir.FullPath;
            await File.WriteAllTextAsync(dir.GetFullPath("a.txt"), "content".AsMemory());
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task ImplicitConversions()
    {
        await using var dir = TemporaryDirectory.Create();
        FullPath path = dir;
        string pathStr = dir;
        DirectoryInfo di = dir;

        Assert.Equal(dir.FullPath.Value, path.Value);
        Assert.Equal(dir.FullPath.Value, pathStr);
        Assert.Equal(dir.FullPath.Value, di.FullName);
    }

    [Fact]
    public async Task SlashOperator()
    {
        await using var dir = TemporaryDirectory.Create();
        var path = dir / "subdir" / "file.txt";
        Assert.Equal(dir.GetFullPath("subdir/file.txt"), path);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    [InlineData("..")]
    public void GetFullPathRejectsPathsOutsideTheDirectory(string relativePath)
    {
        using var dir = TemporaryDirectory.Create();

        Assert.Throws<ArgumentException>(() => dir.GetFullPath(relativePath));
        Assert.Throws<ArgumentException>(() => dir.CreateTextFile(relativePath, "content"));
        Assert.Throws<ArgumentException>(() => dir.CreateEmptyFile(relativePath));
        Assert.Throws<ArgumentException>(() => dir.CreateDirectory(relativePath));
        Assert.Throws<ArgumentException>(() => dir / relativePath);
    }

    [Fact]
    public void GetFullPathRejectsARootedPath()
    {
        using var dir = TemporaryDirectory.Create();
        var rooted = FullPath.Combine(Path.GetTempPath(), "escape.txt");

        Assert.Throws<ArgumentException>(() => dir.GetFullPath(rooted.Value));
    }

    [Fact]
    public void GetFullPathAllowsPathsInsideTheDirectory()
    {
        using var dir = TemporaryDirectory.Create();

        Assert.Equal(dir.FullPath / "a.txt", dir.GetFullPath("a.txt"));
        Assert.Equal(dir.FullPath / "sub" / "a.txt", dir.GetFullPath("sub/a.txt"));
        Assert.Equal(dir.FullPath / "a.txt", dir.GetFullPath("sub/../a.txt"));
        Assert.Equal(dir.FullPath, dir.GetFullPath(""));
    }

    [Fact]
    public void CreateTextFileReturnsThePathAndWritesTheContent()
    {
        using var dir = TemporaryDirectory.Create();

        var path = dir.CreateTextFile("a.txt", "content");

        Assert.Equal(dir.GetFullPath("a.txt"), path);
        Assert.Equal("content", File.ReadAllText(path));
    }

    [Fact]
    public async Task CreateTextFileAsyncReturnsThePathAndWritesTheContent()
    {
        await using var dir = TemporaryDirectory.Create();

        var path = await dir.CreateTextFileAsync("a.txt", "content", XunitCancellationToken);

        Assert.Equal(dir.GetFullPath("a.txt"), path);
        Assert.Equal("content", await File.ReadAllTextAsync(path, XunitCancellationToken));
    }

    [Fact]
    public void CreateEmptyFileReturnsThePathAndCreatesAnEmptyFile()
    {
        using var dir = TemporaryDirectory.Create();

        var path = dir.CreateEmptyFile("a.txt");

        Assert.Equal(dir.GetFullPath("a.txt"), path);
        Assert.Empty(File.ReadAllText(path));
    }

    [Fact]
    public void CreateDirectoryReturnsThePathAndCreatesTheDirectory()
    {
        using var dir = TemporaryDirectory.Create();

        var path = dir.CreateDirectory("sub/nested");

        Assert.Equal(dir.GetFullPath("sub/nested"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CreateFileCreatesTheParentDirectories()
    {
        using var dir = TemporaryDirectory.Create();

        var path = dir.CreateTextFile("sub/nested/a.txt", "content");

        Assert.True(File.Exists(path));
        Assert.True(Directory.Exists(dir.GetFullPath("sub/nested")));
    }

    [Fact]
    public void CreateUnderAnExplicitRootDirectory()
    {
        using var parent = TemporaryDirectory.Create();
        var root = parent.GetFullPath("root");

        using var dir = TemporaryDirectory.Create(root);

        Assert.Equal(root, dir.FullPath.Parent);
        Assert.True(Directory.Exists(dir.FullPath));
    }

    [Fact]
    public void TemporaryFileDisposedDeletesFile()
    {
        FullPath path;
        using (var file = TemporaryFile.Create())
        {
            path = file.FullPath;
            File.WriteAllText(file.FullPath, "content");
            Assert.True(File.Exists(path));
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TemporaryFileDisposeAsyncDeletesFile()
    {
        FullPath path;
        await using (var file = TemporaryFile.Create())
        {
            path = file.FullPath;
            await File.WriteAllTextAsync(file.FullPath, "content".AsMemory(), XunitCancellationToken);
            Assert.True(File.Exists(path));
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TemporaryFileCreateWithFileName()
    {
        using var file = TemporaryFile.Create("custom.txt");
        var expectedRoot = FullPath.Combine(Path.GetTempPath(), "MezTF");
        Assert.Equal(expectedRoot, file.FullPath.Parent.Parent);
        Assert.True(File.Exists(file.FullPath));
    }

    [Fact]
    public void TemporaryFileCreateWithFullPath()
    {
        var fullPath = FullPath.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");
        using var file = TemporaryFile.Create(fullPath);
        Assert.Equal(fullPath, file.FullPath);
        Assert.True(File.Exists(fullPath));
    }
}
