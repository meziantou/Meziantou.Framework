using Xunit;

namespace Meziantou.Framework.Tests;

// Do not parallelize tests
[Collection("TemporaryDirectoryTests")]
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

#if NET461 || NET462 || NET472
            File.WriteAllText(dir.GetFullPath("a.txt"), "content");
#elif NETCOREAPP3_1_OR_GREATER
            await File.WriteAllTextAsync(dir.GetFullPath("a.txt"), "content");
#else
#error Platform not supported
#endif
        }

        Assert.False(Directory.Exists(path));
    }
}
