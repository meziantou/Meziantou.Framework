namespace Meziantou.Framework.Tests;

public sealed class DefaultConverterTests_ConversionType
{
    [Fact]
    public void Documentation_Example()
    {
        #region DefaultConverter_Example
        var converter = new DefaultConverter();

        // Convert string to int
        Assert.True(converter.TryChangeType("42", typeof(int), CultureInfo.InvariantCulture, out var result));
        Assert.Equal(42, result);

        // Convert byte array to hex string
        converter.ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed;
        Assert.True(converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, typeof(string), CultureInfo.InvariantCulture, out var hex));
        Assert.Equal("0x01020304", hex);

        // Convert hex string to byte array
        Assert.True(converter.TryChangeType("0x01020304", typeof(byte[]), CultureInfo.InvariantCulture, out var bytes));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, (byte[]?)bytes);

        // Parse enum with flags
        Assert.True(converter.TryChangeType("ReadOnly, Hidden", typeof(FileAttributes), CultureInfo.InvariantCulture, out var enumValue));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, enumValue);
        #endregion
    }

    public static TheoryData<Type> NonInstantiableTypes() => new()
    {
        typeof(void),
        typeof(List<>),
        typeof(Dictionary<,>),
        typeof(int).MakeByRefType(),
        typeof(int).MakePointerType(),
    };

    // typeof(void).IsValueType is true, so Activator.CreateInstance used to throw NotSupportedException
    [Theory]
    [MemberData(nameof(NonInstantiableTypes))]
    public void TryConvert_NonInstantiableConversionType_ReturnsFalse(Type conversionType)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;

        var converted = converter.TryChangeType(1, conversionType, cultureInfo, out var value);
        Assert.False(converted);
        Assert.Null(value);
    }

    [Theory]
    [MemberData(nameof(NonInstantiableTypes))]
    public void TryConvert_NonInstantiableConversionType_NullInput_ReturnsFalse(Type conversionType)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;

        var converted = converter.TryChangeType(input: null, conversionType, cultureInfo, out var value);
        Assert.False(converted);
        Assert.Null(value);
    }

    [Fact]
    public void ChangeType_NonInstantiableConversionType_ReturnsDefaultValue()
    {
        var converter = new DefaultConverter();

        Assert.Null(converter.ChangeType(1, typeof(void)));
        Assert.Null(converter.ChangeType(1, typeof(List<>)));
    }

    // A closed generic type is still convertible, so the guard must not be too broad
    [Fact]
    public void TryConvert_ClosedGenericConversionType_IsNotRejected()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;

        var converted = converter.TryChangeType("42", typeof(int?), cultureInfo, out var value);
        Assert.True(converted);
        Assert.Equal(42, value);
    }
}
