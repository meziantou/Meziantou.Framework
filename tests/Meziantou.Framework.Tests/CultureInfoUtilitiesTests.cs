using System.Globalization;
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
            Assert.Equal(expectedResult, actual);
        }

        [Fact]
        public void UseCulture()
        {
            CultureInfoUtilities.UseCulture(CultureInfo.GetCultureInfo("fr-FR"), () =>
            {
                Assert.Equal("12,00", 12.ToString("F2"));
            });
        }
    }
}
