using System.Runtime.InteropServices;
using Xunit;

#pragma warning disable CS0618 // Type or member is obsolete
namespace Meziantou.Framework.Tests;

public class IOUtilitiesTests
{
    [Theory]
    [InlineData(@"c:\dir1\", @"c:\dir1\dir2", true)]
    [InlineData(@"c:\a\", @"c:\dir1\dir2", false)]
    [InlineData(@"c:\a\", @"c:\dir1\..\a\dir2", true)]
    [InlineData(@"c:\dir1", @"c:\dir1", true)]
    public void IsChildPathOf(string parent, string child, bool expectedResult)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = IOUtilities.IsChildPathOf(parent, child);
            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [InlineData(@"c:\dir1", @"c:\dir1", true)]
    [InlineData(@"c:\dir1\", @"c:\dir1\dir2", false)]
    [InlineData(@"c:\a\", @"d:\a\", false)]
    [InlineData(@"c:\a\", @"c:\dir1\..\a\", true)]
    public void ArePathEqual(string path1, string path2, bool expectedResult)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = IOUtilities.ArePathEqual(path1, path2);
            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [InlineData(@"c:\dir1", @"c:\dir1", "")]
    [InlineData(@"c:\dir1\", @"c:\dir1\dir2", "dir2")]
    [InlineData(@"c:\a\", @"d:\a\", @"d:\a\")]
    [InlineData(@"c:\a\", @"c:\dir1\..\a\dir2", "dir2")]
    [InlineData(@"c:\a\b\c\", @"c:\a\dir2", @"..\..\dir2")]
    public void MakeRelativePath(string path1, string path2, string expectedResult)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = IOUtilities.MakeRelativePath(path1, path2);
            Assert.Equal(expectedResult, result);
        }
    }

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
