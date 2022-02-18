using System.Globalization;
using System.Runtime.InteropServices;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_Int32To
{
    [RunIfFact(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void TryConvert_Int32ToCultureInfo_LcidAsInt()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(1033, cultureInfo, out CultureInfo value);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            converted.Should().BeTrue();
            value.Name.Should().Be("en-US");
        }
        else
        {
            converted.Should().BeFalse();
        }
    }

    [Fact]
    public void TryConvert_Int32ToInt64()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(15, cultureInfo, out long value);

        converted.Should().BeTrue();
        value.Should().Be(15L);
    }

    [Fact]
    public void TryConvert_Int32ToInt16()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(15, cultureInfo, out short value);

        converted.Should().BeTrue();
        value.Should().Be(15);
    }

    [Fact]
    public void TryConvert_Int32ToUInt16()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(15, cultureInfo, out ushort value);

        converted.Should().BeTrue();
        value.Should().Be(15);
    }

    [Fact]
    public void TryConvert_Int32ToByteArray()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(0x12345678, cultureInfo, out byte[] value);

        converted.Should().BeTrue();
        value.Should().Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 });
    }
}
