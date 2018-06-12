using System.Globalization;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class DefaultConverterTests_Int32To
    {
        [TestMethod]
        public void TryConvert_Int32ToCultureInfo_LcidAsInt()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(1033, cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("en-US", value.Name);
        }

        [TestMethod]
        public void TryConvert_Int32ToInt64()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(15, cultureInfo, out long value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(15L, value);
        }

        [TestMethod]
        public void TryConvert_Int32ToInt16()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(15, cultureInfo, out short value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual((short)15, value);
        }
    }
}
