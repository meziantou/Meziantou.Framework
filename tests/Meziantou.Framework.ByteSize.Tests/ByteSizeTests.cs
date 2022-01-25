﻿using System;
using System.Globalization;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class ByteSizeTests
    {
        [Theory]
        [InlineData(10L, null, "10B")]
        [InlineData(10L, "", "10B")]
        [InlineData(10L, "B", "10B")]
        [InlineData(1_000L, "kB", "1kB")]
        [InlineData(1_500L, "kB", "1.5kB")]
        [InlineData(1_500L, "kB2", "1.50kB")]
        [InlineData(1_024L, "kiB", "1kiB")]
        [InlineData(1_024L, "fi", "1kiB")]
        [InlineData(1_000_000L, "MB", "1MB")]
        [InlineData(1_000_000L, "f", "1MB")]
        [InlineData(1_510_000L, "f1", "1.5MB")]
        [InlineData(1_510_000L, "f2", "1.51MB")]
        public void ToString_Test(long length, string format, string expectedValue)
        {
            var byteSize = new ByteSize(length);
            var formattedValue = byteSize.ToString(format, CultureInfo.InvariantCulture);

            formattedValue.Should().Be(expectedValue);
            ByteSize.Parse(formattedValue, CultureInfo.InvariantCulture).Should().Be(ByteSize.Parse(expectedValue, CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData(10L, ByteSizeUnit.Byte, "10B")]
        [InlineData(1_000L, ByteSizeUnit.KiloByte, "1kB")]
        [InlineData(1_500L, ByteSizeUnit.KiloByte, "1.5kB")]
        [InlineData(1_024L, ByteSizeUnit.KibiByte, "1kiB")]
        [InlineData(1_000_000L, ByteSizeUnit.MegaByte, "1MB")]
        public void ToString_Unit_Test(long length, ByteSizeUnit unit, string expectedValue)
        {
            var byteSize = new ByteSize(length);
            var formattedValue = byteSize.ToString(unit, CultureInfo.InvariantCulture);

            formattedValue.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData("1", 1L)]
        [InlineData("1b", 1L)]
        [InlineData("1B", 1L)]
        [InlineData("1 B", 1L)]
        [InlineData("1 KB", 1000L)]
        [InlineData("1 kiB", 1024L)]
        [InlineData("1.5 kB", 1500L)]
        public void Parse(string str, long expectedValue)
        {
            var actual = ByteSize.Parse(str, CultureInfo.InvariantCulture);
            var parsed = ByteSize.TryParse(str, CultureInfo.InvariantCulture, out var actualTry);

            using (new AssertionScope())
            {
                actual.Value.Should().Be(expectedValue);
                actualTry.Value.Should().Be(expectedValue);
                parsed.Should().BeTrue();
            }
        }

        [Theory]
        [InlineData("1Bk")]
        [InlineData("1AB")]
        public void Parse_Invalid(string str)
        {
            Func<object> parse = () => ByteSize.Parse(str, CultureInfo.InvariantCulture);
            parse.Should().ThrowExactly<FormatException>();

            var parsed = ByteSize.TryParse(str, CultureInfo.InvariantCulture, out var actualTry);
            parsed.Should().BeFalse();
        }
    }
}
