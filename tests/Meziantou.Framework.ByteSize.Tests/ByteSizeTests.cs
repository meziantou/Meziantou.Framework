namespace Meziantou.Framework.Tests;

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
    [InlineData(1_000_000L, "", "1MB")]
    [InlineData(1_000_000L, "f", "1MB")]
    [InlineData(1_510_000L, "f1", "1.5MB")]
    [InlineData(1_510_000L, "", "1.51MB")]
    [InlineData(1_510_000L, "f2", "1.51MB")]
    [InlineData(1_000_000_000_000_000L, "PB", "1PB")]
    [InlineData(1_000_000_000_000_000_000L, "EB", "1EB")]
    [InlineData(1_000_000_000_000_000_000L, "", "1EB")]
    [InlineData(1_152_921_504_606_846_976L, "EiB", "1EiB")]
    [InlineData(1_152_921_504_606_846_976L, "gi", "1EiB")]
    public void ToString_Test(long length, string? format, string expectedValue)
    {
        var byteSize = new ByteSize(length);
        var formattedValue = byteSize.ToString(format, CultureInfo.InvariantCulture);
        Assert.Equal(expectedValue, formattedValue);
        Assert.Equal(ByteSize.Parse(expectedValue, CultureInfo.InvariantCulture), ByteSize.Parse(formattedValue, CultureInfo.InvariantCulture));
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
        Assert.Equal(expectedValue, formattedValue);
    }

    [Theory]
    [InlineData(-10L, "", "-10B")]
    [InlineData(-1_500L, "", "-1.5kB")]
    [InlineData(-1_500_000_000L, "", "-1.5GB")]
    [InlineData(-1_024L, "gi", "-1kiB")]
    [InlineData(-1_073_741_824L, "gi", "-1GiB")]
    [InlineData(long.MinValue, "gi", "-8EiB")]
    public void ToString_NegativeValue_UsesScaledUnit(long length, string format, string expectedValue)
    {
        Assert.Equal(expectedValue, new ByteSize(length).ToString(format, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1_500L, "", "1.5kB")]
    [InlineData(1_500_000_000L, "", "1.5GB")]
    [InlineData(1_073_741_824L, "gi", "1GiB")]
    public void ToString_NegativeValue_MatchesPositiveApartFromTheSign(long length, string format, string expectedValue)
    {
        Assert.Equal(expectedValue, new ByteSize(length).ToString(format, CultureInfo.InvariantCulture));
        Assert.Equal("-" + expectedValue, new ByteSize(-length).ToString(format, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("1b", 1L)]
    [InlineData("1B", 1L)]
    [InlineData("1 B", 1L)]
    [InlineData("1 KB", 1000L)]
    [InlineData("1 kiB", 1024L)]
    [InlineData("1.5 kB", 1500L)]
    [InlineData("1PB", 1_000_000_000_000_000L)]
    [InlineData("1EB", 1_000_000_000_000_000_000L)]
    [InlineData("1eb", 1_000_000_000_000_000_000L)]
    [InlineData("1 EiB", 1_152_921_504_606_846_976L)]
    [InlineData("1PiB", 1_125_899_906_842_624L)]
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

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("Q")]
    [InlineData("2")]
    public void ToString_InvalidFormat_ThrowsFormatException(string format)
    {
        Assert.Throws<FormatException>(() => new ByteSize(10).ToString(format, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void StringFormat_InvalidFormat_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => string.Format(CultureInfo.InvariantCulture, "{0:ZZZ}", new ByteSize(10)));
    }

    [Theory]
    [InlineData(1_500L, "G", "F")]
    [InlineData(1_500L, "G2", "F2")]
    [InlineData(1_024L, "Gi", "Fi")]
    [InlineData(1_024L, "Gi2", "Fi2")]
    public void ToString_FSpecifier_IsASynonymOfG(long value, string gFormat, string fFormat)
    {
        var size = new ByteSize(value);
        Assert.Equal(size.ToString(gFormat, CultureInfo.InvariantCulture), size.ToString(fFormat, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Operator_Add()
    {
        var result = ByteSize.FromKiloBytes(1) + ByteSize.FromKiloBytes(2);
        Assert.Equal(3000L, result);
    }

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
    [InlineData(1_000_000L, "", "1MB")]
    [InlineData(1_000_000L, "f", "1MB")]
    [InlineData(1_510_000L, "f1", "1.5MB")]
    [InlineData(1_510_000L, "", "1.51MB")]
    [InlineData(1_510_000L, "f2", "1.51MB")]
    [InlineData(1_000_000_000_000_000L, "PB", "1PB")]
    [InlineData(1_000_000_000_000_000_000L, "EB", "1EB")]
    [InlineData(1_000_000_000_000_000_000L, "", "1EB")]
    [InlineData(1_152_921_504_606_846_976L, "EiB", "1EiB")]
    [InlineData(1_152_921_504_606_846_976L, "gi", "1EiB")]
    public void TryFormat_Test(long length, string? format, string expectedValue)
    {
        var byteSize = new ByteSize(length);
        Span<char> destination = stackalloc char[100];
        var success = byteSize.TryFormat(destination, out var charsWritten, format, CultureInfo.InvariantCulture);

        Assert.True(success);
        var formattedValue = destination[..charsWritten].ToString();
        Assert.Equal(expectedValue, formattedValue);
    }

    [Fact]
    public void TryFormat_SmallBuffer_ReturnsFalse()
    {
        var byteSize = new ByteSize(1_000_000L);
        Span<char> destination = stackalloc char[2];
        var success = byteSize.TryFormat(destination, out var charsWritten, "", CultureInfo.InvariantCulture);

        Assert.False(success);
        Assert.Equal(0, charsWritten);
    }

    [Theory]
    [InlineData(9_007_199_254_740_993L, "9007199254740993B")]
    [InlineData(long.MaxValue, "9223372036854775807B")]
    [InlineData(long.MinValue, "-9223372036854775808B")]
    public void ToString_ByteUnit_IsExactAndRoundTrips(long value, string expected)
    {
        var size = new ByteSize(value);

        Assert.Equal(expected, size.ToString("B", CultureInfo.InvariantCulture));
        Assert.Equal(expected, size.ToString(ByteSizeUnit.Byte, CultureInfo.InvariantCulture));
        Assert.Equal(size, ByteSize.Parse(expected, CultureInfo.InvariantCulture));

        Span<char> chars = stackalloc char[64];
        Assert.True(size.TryFormat(chars, out var charsWritten, "B", CultureInfo.InvariantCulture));
        Assert.Equal(expected, chars[..charsWritten].ToString());

        Span<byte> bytes = stackalloc byte[64];
        Assert.True(size.TryFormat(bytes, out var bytesWritten, "B", CultureInfo.InvariantCulture));
        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(bytes[..bytesWritten]));
    }

    [Fact]
    public void ToString_ByteUnit_HonorsTheNumericPrecision()
    {
        Assert.Equal("10.00B", new ByteSize(10).ToString("B2", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(3L)]
    [InlineData(-1L)]
    public void GetValue_UndefinedUnit_ThrowsArgumentOutOfRangeException(long unit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ByteSize(10).GetValue((ByteSizeUnit)unit));
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("1b", 1L)]
    [InlineData("1B", 1L)]
    [InlineData("1 B", 1L)]
    [InlineData("1 KB", 1000L)]
    [InlineData("1 kiB", 1024L)]
    [InlineData("1.5 kB", 1500L)]
    [InlineData("1PB", 1_000_000_000_000_000L)]
    [InlineData("1EB", 1_000_000_000_000_000_000L)]
    [InlineData("1eb", 1_000_000_000_000_000_000L)]
    [InlineData("1 EiB", 1_152_921_504_606_846_976L)]
    [InlineData("1PiB", 1_125_899_906_842_624L)]
    public void Parse_Span(string str, long expectedValue)
    {
        var actual = ByteSize.Parse(str.AsSpan(), CultureInfo.InvariantCulture);
        var parsed = ByteSize.TryParse(str.AsSpan(), CultureInfo.InvariantCulture, out var actualTry);

        Assert.Equal(expectedValue, actual.Value);
        Assert.Equal(expectedValue, actualTry.Value);
        Assert.True(parsed);
    }

    [Theory]
    [InlineData("1Bk")]
    [InlineData("1AB")]
    public void Parse_Span_Invalid(string str)
    {
        Assert.Throws<FormatException>(() => ByteSize.Parse(str.AsSpan(), CultureInfo.InvariantCulture));

        var parsed = ByteSize.TryParse(str.AsSpan(), CultureInfo.InvariantCulture, out var actualTry);
        Assert.False(parsed);
    }

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
    [InlineData(1_000_000L, "", "1MB")]
    [InlineData(1_000_000L, "f", "1MB")]
    [InlineData(1_510_000L, "f1", "1.5MB")]
    [InlineData(1_510_000L, "", "1.51MB")]
    [InlineData(1_510_000L, "f2", "1.51MB")]
    [InlineData(1_000_000_000_000_000L, "PB", "1PB")]
    [InlineData(1_000_000_000_000_000_000L, "EB", "1EB")]
    [InlineData(1_000_000_000_000_000_000L, "", "1EB")]
    [InlineData(1_152_921_504_606_846_976L, "EiB", "1EiB")]
    [InlineData(1_152_921_504_606_846_976L, "gi", "1EiB")]
    public void TryFormat_Utf8_Test(long length, string? format, string expectedValue)
    {
        var byteSize = new ByteSize(length);
        Span<byte> destination = stackalloc byte[100];
        var success = byteSize.TryFormat(destination, out var bytesWritten, format, CultureInfo.InvariantCulture);

        Assert.True(success);
        var formattedValue = System.Text.Encoding.UTF8.GetString(destination[..bytesWritten]);
        Assert.Equal(expectedValue, formattedValue);
    }

    [Fact]
    public void TryFormat_Utf8_SmallBuffer_ReturnsFalse()
    {
        var byteSize = new ByteSize(1_000_000L);
        Span<byte> destination = stackalloc byte[2];
        var success = byteSize.TryFormat(destination, out var bytesWritten, "", CultureInfo.InvariantCulture);

        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("1b", 1L)]
    [InlineData("1B", 1L)]
    [InlineData("1 B", 1L)]
    [InlineData("1 KB", 1000L)]
    [InlineData("1 kiB", 1024L)]
    [InlineData("1.5 kB", 1500L)]
    [InlineData("1PB", 1_000_000_000_000_000L)]
    [InlineData("1EB", 1_000_000_000_000_000_000L)]
    [InlineData("1eb", 1_000_000_000_000_000_000L)]
    [InlineData("1 EiB", 1_152_921_504_606_846_976L)]
    [InlineData("1PiB", 1_125_899_906_842_624L)]
    public void Parse_Utf8(string str, long expectedValue)
    {
        var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(str);
        var actual = ByteSize.Parse(utf8Bytes.AsSpan());
        var parsed = ByteSize.TryParse(utf8Bytes.AsSpan(), out var actualTry);

        Assert.Equal(expectedValue, actual.Value);
        Assert.Equal(expectedValue, actualTry.Value);
        Assert.True(parsed);
    }

    [Theory]
    [InlineData("1Bk")]
    [InlineData("1AB")]
    public void Parse_Utf8_Invalid(string str)
    {
        var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(str);
        Assert.Throws<FormatException>(() => ByteSize.Parse(utf8Bytes.AsSpan()));

        var parsed = ByteSize.TryParse(utf8Bytes.AsSpan(), out var actualTry);
        Assert.False(parsed);
    }

    [Fact]
    public void ToString_RoundTripsThroughParse_ForEveryUnit()
    {
        foreach (var unit in Enum.GetValues<ByteSizeUnit>())
        {
            var size = new ByteSize((long)unit);
            var formatted = size.ToString(unit, CultureInfo.InvariantCulture);

            Assert.Equal(size, ByteSize.Parse(formatted, CultureInfo.InvariantCulture));
            Assert.Equal(size, ByteSize.Parse(formatted.AsSpan(), CultureInfo.InvariantCulture));
            Assert.Equal(size, ByteSize.Parse(System.Text.Encoding.UTF8.GetBytes(formatted).AsSpan()));
        }
    }
}
