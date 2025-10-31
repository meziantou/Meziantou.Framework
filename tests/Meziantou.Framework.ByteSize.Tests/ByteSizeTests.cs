using Xunit;

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
    public void ToString_Test(long length, string format, string expectedValue)
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

    [Fact]
    public void Operator_Add()
    {
        var result = ByteSize.FromKiloBytes(1) + ByteSize.FromKiloBytes(2);
        Assert.Equal(3000L, result);
    }

#if NET6_0_OR_GREATER
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
    public void TryFormat_Test(long length, string format, string expectedValue)
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
#endif

#if NET7_0_OR_GREATER
    [Theory]
    [InlineData("1", 1L)]
    [InlineData("1b", 1L)]
    [InlineData("1B", 1L)]
    [InlineData("1 B", 1L)]
    [InlineData("1 KB", 1000L)]
    [InlineData("1 kiB", 1024L)]
    [InlineData("1.5 kB", 1500L)]
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
#endif

#if NET8_0_OR_GREATER
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
    public void TryFormat_Utf8_Test(long length, string format, string expectedValue)
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
    public void Parse_Utf8(string str, long expectedValue)
    {
        var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(str);
        var actual = ByteSize.Parse(utf8Bytes.AsSpan(), CultureInfo.InvariantCulture);
        var parsed = ByteSize.TryParse(utf8Bytes.AsSpan(), CultureInfo.InvariantCulture, out var actualTry);

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
        Assert.Throws<FormatException>(() => ByteSize.Parse(utf8Bytes.AsSpan(), CultureInfo.InvariantCulture));

        var parsed = ByteSize.TryParse(utf8Bytes.AsSpan(), CultureInfo.InvariantCulture, out var actualTry);
        Assert.False(parsed);
    }
#endif
}
