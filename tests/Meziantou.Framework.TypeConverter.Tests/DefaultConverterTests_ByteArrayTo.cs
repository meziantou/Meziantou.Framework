using System.Globalization;
using Xunit;
using FluentAssertions;

namespace Meziantou.Framework.Tests
{
    public class DefaultConverterTests_ByteArrayTo
    {
        [Fact]
        public void TryConvert_ByteArrayToString_Base64()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            converted.Should().BeTrue();
            value.Should().Be("AQIDBA==");
        }

        [Fact]
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
}
