using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class DefaultConverterTests_ImplicitConverter
    {
        private class ImplicitConverter
        {
            public static implicit operator int(ImplicitConverter _) => 1;
        }

        [TestMethod]
        public void TryConvert_ImplicitConverter_01()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new ImplicitConverter(), cultureInfo, out int value);

            Assert.IsTrue(converted);
            Assert.AreEqual(1, value);
        }
    }
}
