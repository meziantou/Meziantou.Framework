using System.Globalization;
using Xunit;
using FluentAssertions;

namespace Meziantou.Framework.Tests
{
    public class DefaultConverterTests_CultureInfoTo
    {
        [Fact]
        public void TryConvert_CultureInfoToString_UsingInvariantCulture()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), defaultValue: null, CultureInfo.InvariantCulture);

            value.Should().Be("en");
        }

        [Fact]
        public void TryConvert_CultureInfoToString_UsingSpecificCulture()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), defaultValue: null, new CultureInfo("en-US"));

            value.Should().Be("en");
        }
    }
}
