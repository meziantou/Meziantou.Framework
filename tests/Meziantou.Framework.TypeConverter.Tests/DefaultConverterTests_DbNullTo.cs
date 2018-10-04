using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class DefaultConverterTests_DbNullTo
    {
        [TestMethod]
        public void TryConvert_DbNullToNullableInt32()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int? value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(null, value);
        }

        [TestMethod]
        public void TryConvert_DbNullToInt32()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int value);

            Assert.AreEqual(false, converted);
        }

        [TestMethod]
        public void TryConvert_DbNullToString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out string value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(null, value);
        }
    }
}
