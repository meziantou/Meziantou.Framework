using System;
using System.Globalization;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class ByteSizeTests
    {
        [Theory]
        [InlineData(10, null, "10B")]
        [InlineData(10, "", "10B")]
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
            var byteSize = new ByteSize(length);
            var formattedValue = byteSize.ToString(format, CultureInfo.InvariantCulture);

            Assert.Equal(expectedValue, formattedValue);
            Assert.Equal(
                ByteSize.Parse(expectedValue, CultureInfo.InvariantCulture),
                ByteSize.Parse(formattedValue, CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("1b", 1)]
        [InlineData("1B", 1)]
        [InlineData("1 B", 1)]
        [InlineData("1 KB", 1000)]
        [InlineData("1 kiB", 1024)]
        [InlineData("1.5 kB", 1500)]
        public void Parse(string str, long expectedValue)
        {
            var actual = ByteSize.Parse(str, CultureInfo.InvariantCulture);
            var parsed = ByteSize.TryParse(str, CultureInfo.InvariantCulture, out var actualTry);

            Assert.Equal(expectedValue, actual.Value);
            Assert.Equal(expectedValue, actualTry.Value);
            Assert.True(parsed);
        }

        [Theory]
        [InlineData("1Bk")]
        [InlineData("1AB")]
        public void Parse_Invalid(string str)
        {
            Assert.Throws<FormatException>(() => ByteSize.Parse(str, CultureInfo.InvariantCulture));
            var parsed = ByteSize.TryParse(str, CultureInfo.InvariantCulture, out var actualTry);

            Assert.False(parsed);
        }
    }
}
