using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class DefaultConverterTests_ByteArrayTo
    {
        [TestMethod]
        public void TryConvert_ByteArrayToString_Base64()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.IsTrue(converted);
            Assert.AreEqual("AQIDBA==", value);
        }

        [TestMethod]
        public void TryConvert_ByteArrayToString_Base16WithPrefix()
        {
            var converter = new DefaultConverter();
            converter.ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed;
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.IsTrue(converted);
            Assert.AreEqual("0x01020304", value);
        }

        [TestMethod]
        public void TryConvert_ByteArrayToString_Base16WithoutPrefix()
        {
            var converter = new DefaultConverter();
            converter.ByteArrayToStringFormat = ByteArrayToStringFormat.Base16;
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.IsTrue(converted);
            Assert.AreEqual("01020304", value);
        }
    }
}
