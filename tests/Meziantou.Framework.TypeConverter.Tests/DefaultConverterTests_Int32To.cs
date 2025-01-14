using System.Globalization;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_Int32To
{
    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void TryConvert_Int32ToCultureInfo_LcidAsInt()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(1033, cultureInfo, out CultureInfo value);

        if (OperatingSystem.IsWindows())
        {
            Assert.True(converted);
            Assert.Equal("en-US", value.Name);
        }
        else
        {
            Assert.False(converted);
        }
    }

    [Fact]
    public void TryConvert_Int32ToInt64()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(15, cultureInfo, out long value);
        Assert.True(converted);
        Assert.Equal(15L, value);
    }

    [Fact]
    public void TryConvert_Int32ToInt16()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(15, cultureInfo, out short value);
        Assert.True(converted);
        Assert.Equal(15, value);
    }

    [Fact]
    public void TryConvert_Int32ToUInt16()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(15, cultureInfo, out ushort value);
        Assert.True(converted);
        Assert.Equal(15, value);
    }

    [Fact]
    public void TryConvert_Int32ToByteArray()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(0x12345678, cultureInfo, out byte[] value);
        Assert.True(converted);
        Assert.Equal([0x78, 0x56, 0x34, 0x12], value);
    }
}
