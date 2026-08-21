using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ReservedDeviceNameTests
{
    [Theory]
    [RunIf(TestOperatingSystems.Windows)]
    [InlineData(@"C:\temp\CON")]
    [InlineData(@"C:\COM¹")]
    [InlineData(@"C:\LPT²")]
    [InlineData(@"C:\temp\PRN.log")]
    [InlineData(@"C:\temp\AUX.dat")]
    [InlineData(@"C:\temp\NUL.txt")]
    [InlineData(@"C:\temp\COM1.config")]
    [InlineData(@"C:\temp\LPT1.xml")]
    [InlineData(@"C:\folder\CON\file.txt")]
    [InlineData(@"C:\PRN\subfolder\data.bin")]
    public void Value_ContainsReservedName_ReturnsExtendedPath(string pathStr)
    {
        var path = FullPath.FromPath(pathStr);
        var value = path.Value;

        Assert.StartsWith(@"\\?\", value);
    }

    [Theory]
    [RunIf(TestOperatingSystems.Windows)]
    [InlineData(@"C:\ACON")]
    [InlineData(@"C:\CONX")]
    [InlineData(@"C:\COM10")]
    [InlineData(@"C:\normal.txt")]
    public void Value_NonReservedName_ReturnsNormalPath(string pathStr)
    {
        var path = FullPath.FromPath(pathStr);
        var value = path.Value;

        Assert.DoesNotStartWith(@"\\?\", value);
    }

    [Theory]
    [RunIf(TestOperatingSystems.Windows)]
    [InlineData(@"C:\temp\CON")]
    [InlineData(@"C:\temp\PRN.log")]
    [InlineData(@"C:\folder\CON\file.txt")]
    public void RawValue_ContainsReservedName_HasNoExtendedPrefix(string pathStr)
    {
        var path = FullPath.FromPath(pathStr);

        Assert.Equal(pathStr, path.RawValue);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void WithExtension_ReservedName_DoesNotLeakTheExtendedPrefix()
    {
        var path = FullPath.FromPath(@"C:\temp\CON").WithExtension(".txt");

        Assert.Equal(@"C:\temp\CON.txt", path.RawValue);
        Assert.Equal(FullPath.FromPath(@"C:\temp\CON.txt"), path);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void WithExtension_MultipleExtensions_ReservedName_DoesNotLeakTheExtendedPrefix()
    {
        var path = FullPath.FromPath(@"C:\temp\CON.tar.gz").WithExtension(".zip", replaceAllTrailingExtensions: true);

        Assert.Equal(@"C:\temp\CON.zip", path.RawValue);
        Assert.Equal(FullPath.FromPath(@"C:\temp\CON.zip"), path);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void WithName_ReservedName_DoesNotLeakTheExtendedPrefix()
    {
        var path = FullPath.FromPath(@"C:\temp\CON\a.txt").WithName("b.txt");

        Assert.Equal(@"C:\temp\CON\b.txt", path.RawValue);
        Assert.Equal(FullPath.FromPath(@"C:\temp\CON\b.txt"), path);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void WithNameWithoutExtension_ReservedName_DoesNotLeakTheExtendedPrefix()
    {
        var path = FullPath.FromPath(@"C:\temp\CON\a.txt").WithNameWithoutExtension("b");

        Assert.Equal(@"C:\temp\CON\b.txt", path.RawValue);
        Assert.Equal(FullPath.FromPath(@"C:\temp\CON\b.txt"), path);
    }
}
