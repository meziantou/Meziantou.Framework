using System;
using System.Globalization;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DefaultConverterTests_DateTimeTo
    {
        [Fact]
        public void TryConvert_DateTimeToDateTimeOffset()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.CurrentCulture;
            var converted = converter.TryChangeType("6/12/2018 12:00:00 AM -05:00", cultureInfo, out DateTimeOffset value);

            Assert.True(converted);
            Assert.Equal(new DateTimeOffset(2018, 06, 12, 0, 0, 0, TimeSpan.FromHours(-5)), value);
        }
    }
}
