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

            Assert.Equal(Iterations, dirs.Select(dir => dir.FullPath).Distinct().Count());

            foreach (var dir in dirs)
            {
                Assert.All(dirs, dir => Assert.True(Directory.Exists(dir.FullPath)));
            }
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
        var fullPath = FullPath.FromPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp"));
        using var file = TemporaryFile.Create(fullPath);
        Assert.Equal(fullPath, file.FullPath);
        Assert.True(File.Exists(fullPath));
    }
}
