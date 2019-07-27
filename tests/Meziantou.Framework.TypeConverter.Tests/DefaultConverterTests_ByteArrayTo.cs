using System.Globalization;
using Xunit;

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

            Assert.True(converted);
            Assert.Equal("AQIDBA==", value);
        }

        [Fact]
        public void TryConvert_ByteArrayToString_Base16WithPrefix()
        {
            var converter = new DefaultConverter();
            converter.ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed;
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.True(converted);
            Assert.Equal("0x01020304", value);
        }

        [Fact]
        public void TryConvert_ByteArrayToString_Base16WithoutPrefix()
        {
            var converter = new DefaultConverter();
            converter.ByteArrayToStringFormat = ByteArrayToStringFormat.Base16;
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.True(converted);
            Assert.Equal("01020304", value);
        }
    }
}
