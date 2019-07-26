using System;
using System.Globalization;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DefaultConverterTests_DbNullTo
    {
        [Fact]
        public void TryConvert_DbNullToNullableInt32()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int? value);

            Assert.True(converted);
            Assert.Null(value);
        }

        [Fact]
        public void TryConvert_DbNullToInt32()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int _);

            Assert.False(converted);
        }

        [Fact]
        public void TryConvert_DbNullToString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out string value);

            Assert.True(converted);
            Assert.Null(value);
        }
    }
}
