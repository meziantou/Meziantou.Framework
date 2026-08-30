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
    public void TemporaryFileCreateWithFileNameDeletesTheGeneratedDirectory()
    {
        FullPath path;
        using (var file = TemporaryFile.Create("custom.txt"))
        {
            path = file.FullPath;
            Assert.True(Directory.Exists(path.Parent));
        }

        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(path.Parent));
    }

    [Fact]
    public void TemporaryFileCreateWithRelativePathDeletesTheGeneratedDirectory()
    {
        FullPath path;
        using (var file = TemporaryFile.Create(Path.Combine("sub", "custom.txt")))
        {
            path = file.FullPath;
        }

        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(path.Parent));
        Assert.False(Directory.Exists(path.Parent.Parent));
    }

    [Fact]
    public void TemporaryFileCreateKeepsTheSharedRootDirectory()
    {
        FullPath path;
        using (var file = TemporaryFile.Create())
        {
            path = file.FullPath;
        }

        Assert.False(File.Exists(path));
        Assert.True(Directory.Exists(path.Parent));
    }

    [Fact]
    public void TemporaryDirectoryIsOnlyAccessibleByTheCurrentUser()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes only

        using var dir = TemporaryDirectory.Create();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir.FullPath.Value));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir.FullPath.Parent.Value));
    }

    [Fact]
    public void TemporaryFileIsOnlyAccessibleByTheCurrentUser()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes only

        using var file = TemporaryFile.Create();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file.FullPath.Value));
    }

    [Fact]
    public void CreateTightensARootDirectoryAccessibleByOtherUsers()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes only

        using var parent = TemporaryDirectory.Create();
        var sharedRoot = parent.GetFullPath("shared");
        Directory.CreateDirectory(sharedRoot.Value, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        using var dir = TemporaryDirectory.Create(sharedRoot);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(sharedRoot.Value));
    }

    [Fact]
    public void CreateReusesAnOwnerOnlyRootDirectory()
    {
        using var parent = TemporaryDirectory.Create();
        var root = parent.GetFullPath("root");

        using var first = TemporaryDirectory.Create(root);
        using var second = TemporaryDirectory.Create(root);

        Assert.NotEqual(first.FullPath, second.FullPath);
        Assert.Equal(root, first.FullPath.Parent);
        Assert.Equal(root, second.FullPath.Parent);
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
