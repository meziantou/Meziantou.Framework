using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_StringTo
{
    [Flags]
    private enum SampleEnum
    {
        None = 0x0,
        Option1 = 0x1,
        Option2 = 0x2,
        Option3 = 0x4,
    }

    [Fact]
    public void TryConvert_StringToInt32_ValidValue()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("42", cultureInfo, out int value);
        Assert.True(converted);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryConvert_StringToInt32_EmptyString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("", cultureInfo, out int _);
        Assert.False(converted);
    }

    [Fact]
    public void TryConvert_StringToNullableInt32_ValidInteger()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("42", cultureInfo, out int? value);
        Assert.True(converted);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryConvert_StringToNullableInt32_EmptyString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("", cultureInfo, out int? value);
        Assert.True(converted);
        Assert.Null(value);
    }

    [Fact]
    public void TryConvert_StringToEnum_ValueAsString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("2", cultureInfo, out SampleEnum value);
        Assert.True(converted);
        Assert.Equal(SampleEnum.Option2, value);
    }

    [Fact]
    public void TryConvert_StringToEnum_CommaSeparatedString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("Option1, Option2", cultureInfo, out SampleEnum value);
        Assert.True(converted);
        Assert.Equal(SampleEnum.Option1 | SampleEnum.Option2, value);
    }

    [Fact]
    public void TryConvert_StringToEnum_CommaSeparatedStringAndInt()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("Option1, 2", cultureInfo, out SampleEnum value);
        Assert.True(converted);
        Assert.Equal(SampleEnum.Option1 | SampleEnum.Option2, value);
    }

    [Fact]
    public void TryConvert_StringToNullableLong()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("3000000000", cultureInfo, out long? value);
        Assert.True(converted);
        Assert.Equal(3000000000L, value);
    }

    [Fact]
    public void TryConvert_StringToCultureInfo_CultureAsString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("fr-FR", cultureInfo, out CultureInfo? value);
        Assert.True(converted);
        Assert.NotNull(value);
        Assert.Equal("fr-FR", value.Name);
    }

    [Fact]
    public void TryConvert_StringToCultureInfo_NeutralCultureAsString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("es", cultureInfo, out CultureInfo? value);
        Assert.True(converted);
        Assert.NotNull(value);
        Assert.Equal("es", value.Name);
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void TryConvert_StringToCultureInfo_LcidAsString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("1033", cultureInfo, out CultureInfo? value);

        if (OperatingSystem.IsWindows())
        {
            Assert.True(converted);
            Assert.NotNull(value);
            Assert.Equal("en-US", value.Name);
        }
        else
        {
            Assert.False(converted);
        }
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void TryConvert_StringToCultureInfo_InvalidCulture()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("dfgnksdfklgfg", cultureInfo, out CultureInfo? _);
        Assert.False(converted);
    }

    [Fact]
    public void TryConvert_StringToCultureInfo_EmptyString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("", cultureInfo, out CultureInfo? value);
        Assert.True(converted);
        Assert.Equal(CultureInfo.InvariantCulture, value);
    }

    [Fact]
    public void TryConvert_StringToCultureInfo_NullValue()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        string? inputValue = null;
        var converted = converter.TryChangeType<CultureInfo>(inputValue, cultureInfo, out var value);
        Assert.True(converted);
        Assert.Null(value);
    }

    [Fact]
    public void TryConvert_StringToUri_EmptyString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("", cultureInfo, out Uri? value);
        Assert.True(converted);
        Assert.Null(value);
    }

    [Fact]
    public void TryConvert_StringToUri_RelativeUri()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("test.png", cultureInfo, out Uri? value);
        Assert.True(converted);
        Assert.Equal(new Uri("test.png", UriKind.Relative), value);
    }

    [Fact]
    public void TryConvert_StringToUri_AbsoluteUri()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("https://meziantou.net", cultureInfo, out Uri? value);
        Assert.True(converted);
        Assert.Equal(new Uri("https://meziantou.net", UriKind.Absolute), value);
    }

    [Fact]
    public void TryConvert_StringToTimeSpan()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("12:30", cultureInfo, out TimeSpan value);
        Assert.True(converted);
        Assert.Equal(new TimeSpan(12, 30, 0), value);
    }

    [Fact]
    public void TryConvert_StringToGuid()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("2d8a54aa-569b-404f-933b-693918885dba", cultureInfo, out Guid value);
        Assert.True(converted);
        Assert.Equal(new Guid("2d8a54aa-569b-404f-933b-693918885dba"), value);
    }

    [Fact]
    public void TryConvert_StringToDecimal()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("42.24", cultureInfo, out decimal value);
        Assert.True(converted);
        Assert.Equal(42.24m, value);
    }

    [Fact]
    public void TryConvert_StringToByteArray_Base16_Prefixed()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("0x0AFF", cultureInfo, out byte[]? value);
        Assert.True(converted);
        Assert.NotNull(value);
        Assert.Equal([0x0A, 0xFF], value);
    }

    [Fact]
    public void TryConvert_StringToByteArray_Base64()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("AQIDBA==", cultureInfo, out byte[]? value);
        Assert.True(converted);
        Assert.NotNull(value);
        Assert.Equal([1, 2, 3, 4], value);
    }

    [Fact]
    public void TryConvert_StringToByteArray_Base64_Invalid()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("AQIDBA=", cultureInfo, out byte[]? _);
        Assert.False(converted);
    }

    [Fact]
    public void TryConvert_StringToDateTime()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("2018/06/24 14:21:01", cultureInfo, out DateTime value);
        Assert.True(converted);
        Assert.Equal(new DateTime(2018, 06, 24, 14, 21, 01), value);
    }

    [Fact]
    public void TryConvert_StringToDateTimeOffset()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("2018/06/24 14:21:01+0230", cultureInfo, out DateTimeOffset value);
        Assert.True(converted);
        Assert.Equal(new DateTimeOffset(2018, 06, 24, 14, 21, 01, new TimeSpan(2, 30, 0)), value);
    }

    [Fact]
    public void TryConvert_StringToByteArray_Base16()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("0d0102", cultureInfo, out byte[]? value);
        Assert.True(converted);
        Assert.NotNull(value);
        Assert.Equal([0x0d, 0x01, 0x02], value);
    }

    [Fact]
    public void TryConvert_StringToByteArray_Base16Prefixed()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("0x0d01", cultureInfo, out byte[]? value);
        Assert.True(converted);
        Assert.NotNull(value);
        Assert.Equal([0x0d, 0x01], value);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("true")]
    [InlineData("t")]
    [InlineData("yes")]
    [InlineData("y")]
    public void TryConvert_StringToBoolean_TrueValue(string text)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(text, cultureInfo, out bool value);
        Assert.True(converted);
        Assert.True(value);
    }

    [Theory]
    [InlineData("False")]
    [InlineData("false")]
    [InlineData("f")]
    [InlineData("No")]
    [InlineData("n")]
    public void TryConvert_StringToBoolean_FalseValue(string text)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(text, cultureInfo, out bool value);
        Assert.True(converted);
        Assert.False(value);
    }

    [Theory]
    [InlineData("0x1F", 31)]
    [InlineData("0X1F", 31)]
    [InlineData("0x0", 0)]
    [InlineData("0xff", 255)]
    public void TryConvert_HexStringToInt32(string text, int expected)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(text, cultureInfo, out int value);
        Assert.True(converted);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0x1F", 31L)]
    [InlineData("0xFFFFFFFFFF", 1099511627775L)]
    public void TryConvert_HexStringToInt64(string text, long expected)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(text, cultureInfo, out long value);
        Assert.True(converted);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0x0A")]
    [InlineData("0x7F")]
    public void TryConvert_HexStringToByte(string text)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(text, cultureInfo, out byte value);
        Assert.True(converted);
        Assert.Equal(Convert.ToByte(text[2..], fromBase: 16), value);
    }

    // A string starting with 'x' or '0x' used to combine NumberStyles.AllowHexSpecifier with
    // NumberStyles.Integer, which the BCL rejects with an ArgumentException.
    [Theory]
    [InlineData("xyz")]
    [InlineData("X-Men")]
    [InlineData("x")]
    [InlineData("0x")]
    [InlineData("0xZZ")]
    public void TryConvert_StringStartingWithX_DoesNotThrow(string text)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;

        Assert.False(converter.TryChangeType(text, cultureInfo, out int _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out long _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out short _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out byte _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out sbyte _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out uint _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out ulong _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out ushort _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out float _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out double _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out decimal _));
        Assert.False(converter.TryChangeType(text, cultureInfo, out int? _));
    }

    // Hexadecimal notation is not supported by the floating-point types
    [Fact]
    public void TryConvert_HexStringToDouble_ReturnsFalse()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType("0x1F", cultureInfo, out double _);
        Assert.False(converted);
    }

    // A word starting with 'x' is no longer treated as a hexadecimal literal
    [Fact]
    public void TryConvert_StringWithoutHexPrefix_IsNotParsedAsHexadecimal()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        Assert.False(converter.TryChangeType("xA", cultureInfo, out byte _));
    }

    // These used to reach the TypeDescriptor fallback because NumberStyles.Integer
    // rejects the decimal separator, the thousands separator and the exponent
    [Theory]
    [InlineData("1.5e3", 1500d)]
    [InlineData("42.24", 42.24d)]
    [InlineData("-0.5", -0.5d)]
    public void TryConvert_StringToDouble_SupportsFloatingPointNotation(string text, double expected)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(text, cultureInfo, out double value);
        Assert.True(converted);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("42.24")]
    [InlineData("1,234.56")]
    public void TryConvert_StringToDecimal_SupportsSeparators(string text)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(text, cultureInfo, out decimal value);
        Assert.True(converted);
        Assert.Equal(decimal.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, cultureInfo), value);
    }
}
