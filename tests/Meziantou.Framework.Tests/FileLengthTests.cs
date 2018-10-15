using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class FileLengthTests
    {
        [DataTestMethod]
        [DataRow(10, "B", "10")]
        [DataRow(1000, "kB", "1")]
        [DataRow(1500, "kB", "1.5")]
        [DataRow(1500, "kB2", "1.50")]
        [DataRow(1024, "kiB", "1")]
        [DataRow(1000000, "MB", "1")]
        public void ToString_Test(long length, string format, string expectedValue)
        {
            var fileLength = new FileLength(length);
            var actual = fileLength.ToString(format, CultureInfo.InvariantCulture);

            Assert.AreEqual(expectedValue, actual);
        }
    }
}
