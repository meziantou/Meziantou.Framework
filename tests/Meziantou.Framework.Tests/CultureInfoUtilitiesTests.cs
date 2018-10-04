using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class CultureInfoUtilitiesTests
    {
        [TestMethod]
        [DataRow("fr-FR", "fr-CA", true)]
        [DataRow("fr-FR", "en-CA", false)]
        public void NeutralEquals(string left, string right, bool expectedResult)
        {
            var actual = CultureInfo.GetCultureInfo(left).NeutralEquals(CultureInfo.GetCultureInfo(right));
            Assert.AreEqual(expectedResult, actual);
        }

        [TestMethod]
        public void UseCulture()
        {
            CultureInfoUtilities.UseCulture(CultureInfo.GetCultureInfo("fr-FR"), () =>
            {
                Assert.AreEqual("12,00", 12.ToString("F2"));
            });
        }
    }
}
