using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class CultureInfoUtilitiesTests
    {
        [Theory]
        [InlineData("fr-FR", "fr-CA", true)]
        [InlineData("fr-FR", "en-CA", false)]
        public void NeutralEquals(string left, string right, bool expectedResult)
        {
            var actual = CultureInfo.GetCultureInfo(left).NeutralEquals(CultureInfo.GetCultureInfo(right));
            actual.Should().Be(expectedResult);
        }

        [Fact]
        public void UseCulture()
        {
            CultureInfoUtilities.UseCulture(CultureInfo.GetCultureInfo("fr-FR"), () =>
            {
                12.ToString("F2", CultureInfo.CurrentCulture).Should().Be("12,00");
            });
        }
    }
}
