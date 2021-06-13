using System;
using System.Globalization;
using FluentAssertions;
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

            converted.Should().BeTrue();
            value.Should().Be(new DateTimeOffset(2018, 06, 12, 0, 0, 0, TimeSpan.FromHours(-5)));
        }
    }
}
