using System.ComponentModel;

namespace Meziantou.Framework.Tests;

public sealed class DefaultConverterTests_TypeDescriptor
{
    private sealed class CustomTypeConverter : TypeConverter
    {
        public static Dummy Instance { get; } = new Dummy();

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof(int);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            return Instance;
        }

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        {
            return destinationType == typeof(int);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            return 10;
        }
    }

    [TypeConverter(typeof(CustomTypeConverter))]
    private sealed class Dummy
    {
    }

    private sealed class ThrowingTypeConverter : TypeConverter
    {
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => true;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            throw new InvalidOperationException("This converter always throws");
        }
    }

    [TypeConverter(typeof(ThrowingTypeConverter))]
    private sealed class ThrowingDummy
    {
        public override string ToString() => "fallback";
    }

    [Fact]
    public void TryConvert_TypeConverter_ConvertTo()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new Dummy(), cultureInfo, out int value);
        Assert.True(converted);
        Assert.Equal(10, value);
    }

    [Fact]
    public void TryConvert_TypeConverter_ConvertFrom()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(1, cultureInfo, out Dummy? value);
        Assert.True(converted);
        Assert.Equal(CustomTypeConverter.Instance, value);
    }

    [Fact]
    public void TryConvert_TypeConverter_ConvertFrom_NoMatchingTypeConverter()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;

        var converted = converter.TryChangeType("", cultureInfo, out Dummy? _);
        Assert.False(converted);
    }

    // TypeConverter.CanConvertTo(typeof(string)) returns true by default for every type,
    // so an exception thrown by a user converter used to escape TryChangeType
    [Fact]
    public void TryConvert_ToString_ThrowingTypeConverter_DoesNotThrow()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(new ThrowingDummy(), cultureInfo, out string? value);
        Assert.True(converted);
        Assert.Equal("fallback", value);
    }

    // The two-argument TypeConverter.ConvertTo overload is hardcoded to CultureInfo.CurrentCulture,
    // so the provider passed by the caller used to be ignored
    [Theory]
    [InlineData("fr-FR", "1234,56")]
    [InlineData("en-US", "1234.56")]
    public void TryConvert_DecimalToString_UsesTheProvider(string cultureName, string expected)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.GetCultureInfo(cultureName);
        var converted = converter.TryChangeType(1234.56m, cultureInfo, out string? value);
        Assert.True(converted);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("fr-FR", "02/01/2020")]
    [InlineData("en-US", "1/2/2020")]
    public void TryConvert_DateTimeToString_UsesTheProvider(string cultureName, string expected)
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.GetCultureInfo(cultureName);
        var converted = converter.TryChangeType(new DateTime(2020, 1, 2), cultureInfo, out string? value);
        Assert.True(converted);
        Assert.Equal(expected, value);
    }
}
