#pragma warning disable CS0618 // Type or member is obsolete
namespace Meziantou.Framework.Tests;

public class IOUtilitiesTests
{
    [Theory]
    [InlineData("sample.txt", "sample.txt")]
    [InlineData("sample/.txt", "sample_x47_.txt")]
    [InlineData("COM1", "_COM1_")]
    public void ToValidFileName(string fileName, string expectedResult)
    {
        var result = IOUtilities.ToValidFileName(fileName);
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void DeleteIgnoresADirectoryRemovedAfterTheExistsCheck()
    {
        var directoryInfo = CreateDirectoryRemovedAfterTheExistsCheck();

        IOUtilities.Delete(directoryInfo);
    }

    [Fact]
    public async Task DeleteAsyncIgnoresADirectoryRemovedAfterTheExistsCheck()
    {
        var directoryInfo = CreateDirectoryRemovedAfterTheExistsCheck();

        await IOUtilities.DeleteAsync(directoryInfo, XunitCancellationToken);
    }

    [Fact]
    public void DeleteDoesNotFollowADirectorySymbolicLink()
    {
        var (link, target, targetFile) = CreateDirectorySymbolicLink();

        IOUtilities.Delete(new DirectoryInfo(link));

        Assert.False(Directory.Exists(link));
        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(targetFile));
        Directory.Delete(target, recursive: true);
    }

    [Fact]
    public async Task DeleteAsyncDoesNotFollowADirectorySymbolicLink()
    {
        var (link, target, targetFile) = CreateDirectorySymbolicLink();

        await IOUtilities.DeleteAsync(new DirectoryInfo(link), XunitCancellationToken);

        Assert.False(Directory.Exists(link));
        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(targetFile));
        Directory.Delete(target, recursive: true);
    }

    private static (string Link, string Target, string TargetFile) CreateDirectorySymbolicLink()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);

        var targetFile = Path.Combine(target, "content.txt");
        File.WriteAllText(targetFile, "content");

        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, target);

        return (link, target, targetFile);
    }

    // FileSystemInfo.Exists is cached, so this reproduces the window between the Exists check
    // and the enumeration of the directory content, without depending on timing.
    private static DirectoryInfo CreateDirectoryRemovedAfterTheExistsCheck()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        var directoryInfo = new DirectoryInfo(path);
        Assert.True(directoryInfo.Exists);
        Directory.Delete(path);

        return directoryInfo;
    }
}
