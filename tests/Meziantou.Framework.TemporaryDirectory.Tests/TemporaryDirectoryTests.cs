using FluentAssertions;
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

            dirs.Select(dir => dir.FullPath).Distinct().Should().HaveCount(Iterations);

            foreach (var dir in dirs)
            {
                dirs.Should().OnlyContain(dir => Directory.Exists(dir.FullPath));
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

        Directory.Exists(path).Should().BeFalse();
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

        Directory.Exists(path).Should().BeFalse();
    }
}
