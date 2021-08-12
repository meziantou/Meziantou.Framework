using System;
using System.Globalization;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Meziantou.Framework.Tests;

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

        formattedValue.Should().Be(expectedValue);
        ByteSize.Parse(formattedValue, CultureInfo.InvariantCulture).Should().Be(ByteSize.Parse(expectedValue, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(10, ByteSizeUnit.Byte, "10B")]
    [InlineData(1_000, ByteSizeUnit.KiloByte, "1kB")]
    [InlineData(1_500, ByteSizeUnit.KiloByte, "1.5kB")]
    [InlineData(1_024, ByteSizeUnit.KibiByte, "1kiB")]
    [InlineData(1_000_000, ByteSizeUnit.MegaByte, "1MB")]
    public void ToString_Unit_Test(long length, ByteSizeUnit unit, string expectedValue)
    {
        var byteSize = new ByteSize(length);
        var formattedValue = byteSize.ToString(unit, CultureInfo.InvariantCulture);

        formattedValue.Should().Be(expectedValue);
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
