using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_ByteArrayTo
{
    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void TryConvert_ByteArrayToString_Base64()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

        converted.Should().BeTrue();
        value.Should().Be("AQIDBA==");
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void TryConvert_ByteArrayToString_Base16WithPrefix()
    {
        var converter = new DefaultConverter
        {
            ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed,
        };
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

        converted.Should().BeTrue();
        value.Should().Be("0x01020304");
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void TryConvert_ByteArrayToString_Base16WithoutPrefix()
    {
        var converter = new DefaultConverter
        {
            ByteArrayToStringFormat = ByteArrayToStringFormat.Base16,
        };
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

        converted.Should().BeTrue();
        value.Should().Be("01020304");
    }
}
