using System.Globalization;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class DefaultConverterTests_CultureInfoTo
    {
        [TestMethod]
        public void TryConvert_CultureInfoToString_UsingInvariantCulture()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), null, CultureInfo.InvariantCulture);

            Assert.AreEqual("en", value);
        }

        [TestMethod]
        public void TryConvert_CultureInfoToString_UsingSpecificCulture()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), null, new CultureInfo("en-US"));

            Assert.AreEqual("en", value);
        }
    }
}
