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
