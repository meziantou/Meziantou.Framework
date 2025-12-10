using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ReservedDeviceNameTests
{
    [Theory]
    [RunIf(FactOperatingSystem.Windows)]
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
        
        Assert.StartsWith(@"\\?\", value, StringComparison.Ordinal);
    }

    [Theory]
    [RunIf(FactOperatingSystem.Windows)]
    [InlineData(@"C:\ACON")]
    [InlineData(@"C:\CONX")]
    [InlineData(@"C:\COM10")]
    [InlineData(@"C:\normal.txt")]
    public void Value_NonReservedName_ReturnsNormalPath(string pathStr)
    {
        var path = FullPath.FromPath(pathStr);
        var value = path.Value;
        
        Assert.False(value.StartsWith(@"\\?\", StringComparison.Ordinal));
    }
}
