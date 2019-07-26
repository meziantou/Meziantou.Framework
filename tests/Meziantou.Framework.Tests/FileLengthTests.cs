using System.Globalization;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class FileLengthTests
    {
        [Theory]
        [InlineData(10, "B", "10")]
        [InlineData(1000, "kB", "1")]
        [InlineData(1500, "kB", "1.5")]
        [InlineData(1500, "kB2", "1.50")]
        [InlineData(1024, "kiB", "1")]
        [InlineData(1000000, "MB", "1")]
        public void ToString_Test(long length, string format, string expectedValue)
        {
            var fileLength = new FileLength(length);
            var actual = fileLength.ToString(format, CultureInfo.InvariantCulture);

            Assert.Equal(expectedValue, actual);
        }
    }
}
