using System.Globalization;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class ByteSizeTests
    {
        [Theory]
        [InlineData(10, "B", "10B")]
        [InlineData(1_000, "kB", "1kB")]
        [InlineData(1_500, "kB", "1.5kB")]
        [InlineData(1_500, "kB2", "1.50kB")]
        [InlineData(1_024, "kiB", "1kiB")]
        [InlineData(1_024, "fi", "1kiB")]
        [InlineData(1_000_000, "MB", "1MB")]
        [InlineData(1_000_000, "f", "1MB")]
        [InlineData(1_510_000, "f1", "1.5MB")]
        [InlineData(1_510_000, "f2", "1.51MB")]
        public void ToString_Test(long length, string format, string expectedValue)
        {
            var fileLength = new ByteSize(length);
            var actual = fileLength.ToString(format, CultureInfo.InvariantCulture);

            Assert.Equal(expectedValue, actual);
        }
    }
}
