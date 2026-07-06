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
}
