using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ConvertUtilitiesTests
{
    // IConverter is public, so a third party can report a failure with a value of any type
    private sealed class MismatchedFailureConverter : IConverter
    {
        public bool TryChangeType(object? input, Type conversionType, IFormatProvider? provider, out object? value)
        {
            value = "not an int";
            return false;
        }
    }

    private sealed class NullFailureConverter : IConverter
    {
        public bool TryChangeType(object? input, Type conversionType, IFormatProvider? provider, out object? value)
        {
            value = null;
            return false;
        }
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Documentation_Example()
    {
        #region ConvertUtilities_Example
        // Using TryChangeType
        Assert.True(ConvertUtilities.TryChangeType("42", CultureInfo.InvariantCulture, out int value));
        Assert.Equal(42, value);

        // Using ChangeType with default value
        var result = ConvertUtilities.ChangeType<int>("invalid", defaultValue: 0, CultureInfo.InvariantCulture);
        Assert.Equal(0, result);

        // Convert with culture-specific formatting
        var cultureInfo = CultureInfo.GetCultureInfo("fr-FR");
        var number = ConvertUtilities.ChangeType<decimal>("1234,56", defaultValue: 0m, provider: cultureInfo);
        Assert.Equal(1234.56m, number);
        #endregion
    }

    [Fact]
    public void TryChangeType_ConverterFailsWithMismatchedValue_ReturnsDefault()
    {
        var converter = new MismatchedFailureConverter();

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out int intValue));
        Assert.Equal(0, intValue);

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out Guid guidValue));
        Assert.Equal(Guid.Empty, guidValue);

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out int? nullableValue));
        Assert.Null(nullableValue);

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out Uri? referenceValue));
        Assert.Null(referenceValue);
    }

    [Fact]
    public void TryChangeType_ConverterFailsWithMatchingValue_KeepsTheValue()
    {
        var converter = new MismatchedFailureConverter();

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out string? value));
        Assert.Equal("not an int", value);
    }

    [Fact]
    public void TryChangeType_ConverterFailsWithNull_ReturnsDefault()
    {
        var converter = new NullFailureConverter();

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out int intValue));
        Assert.Equal(0, intValue);

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out int? nullableValue));
        Assert.Null(nullableValue);

        Assert.False(converter.TryChangeType("x", CultureInfo.InvariantCulture, out string? referenceValue));
        Assert.Null(referenceValue);
    }
}
