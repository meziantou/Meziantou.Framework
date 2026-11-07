namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_ByteArrayTo
{
    [Fact]
    public void ReadmeExample_ByteArrayConversions()
    {
        #region TypeConverter_ByteArrayConversions
        var bytes = new byte[] { 1, 2, 3, 4 };

        // Default: Base64
        var converter1 = new DefaultConverter();
        converter1.TryChangeType(bytes, null, out string? base64);
        Assert.Equal("AQIDBA==", base64);

        // Hexadecimal without prefix
        var converter2 = new DefaultConverter
        {
            ByteArrayToStringFormat = ByteArrayToStringFormat.Base16,
        };
        converter2.TryChangeType(bytes, null, out string? hex);
        Assert.Equal("01020304", hex);

        // Hexadecimal with 0x prefix
        var converter3 = new DefaultConverter
        {
            ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed,
        };
        converter3.TryChangeType(bytes, null, out string? hexPrefixed);
        Assert.Equal("0x01020304", hexPrefixed);

        // Convert from hexadecimal string to byte array
        ConvertUtilities.TryChangeType("0x01020304", CultureInfo.InvariantCulture, out byte[]? result);
        Assert.Equal([1, 2, 3, 4], result);

        // Convert from Base64 string to byte array
        ConvertUtilities.TryChangeType("AQIDBA==", CultureInfo.InvariantCulture, out byte[]? result2);
        Assert.Equal([1, 2, 3, 4], result2);
        #endregion
    }

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
}
