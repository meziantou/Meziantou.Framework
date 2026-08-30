namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_ByteArrayTo
{
    [Fact]
    public void TryConvert_ByteArrayToString_Base64()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string? value);
        Assert.True(converted);
        Assert.Equal("AQIDBA==", value);
    }

    [Fact]
    public void TryConvert_ByteArrayToString_Base16WithPrefix()
    {
        var converter = new DefaultConverter
        {
            ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed,
        };
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string? value);
        Assert.True(converted);
        Assert.Equal("0x01020304", value);
    }

    [Fact]
    public void TryConvert_ByteArrayToString_Base16WithoutPrefix()
    {
        var converter = new DefaultConverter
        {
            ByteArrayToStringFormat = ByteArrayToStringFormat.Base16,
        };
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string? value);
        Assert.True(converted);
        Assert.Equal("01020304", value);
    }

    // These types have a byte[] encoder but had no decoder, so the round-trip silently failed
    [Fact]
    public void TryConvert_DateTimeToByteArrayAndBack()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var expected = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        Assert.True(converter.TryChangeType(expected, cultureInfo, out byte[]? bytes));
        Assert.NotNull(bytes);
        Assert.True(converter.TryChangeType(bytes, cultureInfo, out DateTime value));
        Assert.Equal(expected, value);
        Assert.Equal(expected.Kind, value.Kind);
    }

    [Fact]
    public void TryConvert_TimeSpanToByteArrayAndBack()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var expected = new TimeSpan(1, 2, 3, 4, 5);

        Assert.True(converter.TryChangeType(expected, cultureInfo, out byte[]? bytes));
        Assert.NotNull(bytes);
        Assert.True(converter.TryChangeType(bytes, cultureInfo, out TimeSpan value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-42.24")]
    [InlineData("0")]
    [InlineData("79228162514264337593543950335")]
    public void TryConvert_DecimalToByteArrayAndBack(string text)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var expected = decimal.Parse(text, NumberStyles.Float, cultureInfo);

        Assert.True(converter.TryChangeType(expected, cultureInfo, out byte[]? bytes));
        Assert.NotNull(bytes);
        Assert.True(converter.TryChangeType(bytes, cultureInfo, out decimal value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryConvert_GuidToByteArrayAndBack()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var expected = new Guid("2d8a54aa-569b-404f-933b-693918885dba");

        Assert.True(converter.TryChangeType(expected, cultureInfo, out byte[]? bytes));
        Assert.NotNull(bytes);
        Assert.True(converter.TryChangeType(bytes, cultureInfo, out Guid value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    public void TryConvert_ByteArrayOfWrongLength_ReturnsFalse(int length)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var bytes = new byte[length];

        Assert.False(converter.TryChangeType(bytes, cultureInfo, out DateTime _));
        Assert.False(converter.TryChangeType(bytes, cultureInfo, out TimeSpan _));
        Assert.False(converter.TryChangeType(bytes, cultureInfo, out decimal _));
    }

    // DateTime.FromBinary rejects these, and new decimal(int[]) rejects an invalid scale
    [Fact]
    public void TryConvert_ByteArrayWithInvalidPayload_ReturnsFalse()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;

        var invalidDateTime = BitConverter.GetBytes(long.MaxValue);
        Assert.False(converter.TryChangeType(invalidDateTime, cultureInfo, out DateTime _));

        var invalidDecimal = new byte[16];
        invalidDecimal[14] = 0xFF; // scale byte, valid range is 0-28
        Assert.False(converter.TryChangeType(invalidDecimal, cultureInfo, out decimal _));
    }
}
