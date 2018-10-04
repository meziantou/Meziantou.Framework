using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class DefaultConverterTests_DateTimeTo
    {
        [TestMethod]
        public void TryConvert_DateTimeToDateTimeOffset()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new DateTime(2018, 06, 12), cultureInfo, out DateTimeOffset value);

            Assert.AreEqual(true, converted);
            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            Assert.AreEqual(new DateTimeOffset(2018, 06, 12, 0, 0, 0, offset), value);
        }
    }
}
