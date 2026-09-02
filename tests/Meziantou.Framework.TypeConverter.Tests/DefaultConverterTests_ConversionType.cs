namespace Meziantou.Framework.Tests;

public sealed class DefaultConverterTests_ConversionType
{
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
