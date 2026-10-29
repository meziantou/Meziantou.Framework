using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ConvertUtilitiesTests
{
    // The example parses a number with the French culture
    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void ReadmeExample_BasicConversions()
    {
        #region TypeConverter_BasicConversions
        // Using TryChangeType (recommended)
        Assert.True(ConvertUtilities.TryChangeType("42", CultureInfo.InvariantCulture, out int value));
        Assert.Equal(42, value);

        // Using ChangeType with default value
        var result = ConvertUtilities.ChangeType<int>("invalid", defaultValue: 0, provider: CultureInfo.InvariantCulture);
        Assert.Equal(0, result);

        // Convert with culture-specific formatting
        var cultureInfo = CultureInfo.GetCultureInfo("fr-FR");
        var number = ConvertUtilities.ChangeType<decimal>("1234,56", defaultValue: 0m, provider: cultureInfo);
        Assert.Equal(1234.56m, number);
        #endregion
    }

    [Fact]
    public void Documentation_GettingStarted()
    {
        #region TypeConverter_GettingStarted
        // TryChangeType reports whether the conversion succeeded
        Assert.True(ConvertUtilities.TryChangeType("42", CultureInfo.InvariantCulture, out int number));
        Assert.Equal(42, number);
        Assert.False(ConvertUtilities.TryChangeType("forty-two", CultureInfo.InvariantCulture, out int _));

        // ChangeType returns a default value when the conversion fails
        Assert.Equal(-1, ConvertUtilities.ChangeType("forty-two", defaultValue: -1, CultureInfo.InvariantCulture));

        // The target type can be known only at run time
        Assert.True(ConvertUtilities.TryChangeType("3.5", typeof(double), CultureInfo.InvariantCulture, out var value));
        Assert.Equal(3.5, value);
        #endregion
    }

    [Fact]
    public void Documentation_SupportedConversions()
    {
        #region TypeConverter_SupportedConversions
        var culture = CultureInfo.InvariantCulture;

        // Numbers, including hexadecimal integers
        Assert.Equal(255, ConvertUtilities.ChangeType("0xFF", defaultValue: 0, culture));
        Assert.Equal(1234.5m, ConvertUtilities.ChangeType("1,234.5", defaultValue: 0m, culture));

        // Booleans accept numbers and words
        Assert.True(ConvertUtilities.ChangeType("yes", defaultValue: false, culture));
        Assert.True(ConvertUtilities.ChangeType("1", defaultValue: false, culture));
        Assert.False(ConvertUtilities.ChangeType("N", defaultValue: true, culture));

        // Enums, by name (ignoring case), by value, or as a list of flags
        Assert.Equal(DayOfWeek.Friday, ConvertUtilities.ChangeType("friday", defaultValue: DayOfWeek.Sunday, culture));
        Assert.Equal(DayOfWeek.Friday, ConvertUtilities.ChangeType(5, defaultValue: DayOfWeek.Sunday, culture));
        Assert.Equal(FileAccess.ReadWrite, ConvertUtilities.ChangeType("Read, Write", defaultValue: FileAccess.Read, culture));

        // Nullable types: an empty string is null
        Assert.Null(ConvertUtilities.ChangeType<int?>("", defaultValue: 0, culture));
        Assert.Equal(12, ConvertUtilities.ChangeType<int?>("12", defaultValue: null, culture));

        // Guid, TimeSpan, DateTimeOffset
        Assert.Equal(new Guid("1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3"), ConvertUtilities.ChangeType("1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3", defaultValue: Guid.Empty, culture));
        Assert.Equal(TimeSpan.FromMinutes(90), ConvertUtilities.ChangeType("01:30:00", defaultValue: TimeSpan.Zero, culture));
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero), ConvertUtilities.ChangeType("2024-06-15T12:00:00Z", defaultValue: DateTimeOffset.MinValue, culture));

        // Types that have a TypeConverter, such as Uri and Version
        Assert.Equal(new Uri("https://www.meziantou.net"), ConvertUtilities.ChangeType<Uri?>("https://www.meziantou.net", defaultValue: null, culture));
        Assert.Equal(new Version(1, 2, 3), ConvertUtilities.ChangeType<Version?>("1.2.3", defaultValue: null, culture));

        // Any value to string
        Assert.Equal("3.5", ConvertUtilities.ChangeType<string?>(3.5, defaultValue: null, culture));
        #endregion
    }

    [Fact]
    public void Documentation_ImplicitOperator()
    {
        #region TypeConverter_ImplicitOperatorUsage
        var value = ConvertUtilities.ChangeType<double>(new Percentage(25), defaultValue: 0, CultureInfo.InvariantCulture);
        Assert.Equal(0.25, value);
        #endregion
    }

    #region TypeConverter_ImplicitOperatorType
    private sealed class Percentage(double value)
    {
        public double Value { get; } = value;

        // DefaultConverter uses the implicit operators declared by the type of the value to convert
        public static implicit operator double(Percentage percentage) => percentage.Value / 100;
    }
    #endregion

    [Fact]
    public void Documentation_CustomConverter()
    {
        #region TypeConverter_CustomConverterUsage
        var converter = new YesNoConverter();

        Assert.True(converter.TryChangeType(true, CultureInfo.InvariantCulture, out string? text));
        Assert.Equal("Yes", text);

        // The other conversions are unchanged
        Assert.Equal(42, converter.ChangeType("42", defaultValue: 0, CultureInfo.InvariantCulture));
        #endregion
    }

    #region TypeConverter_CustomConverter
    private sealed class YesNoConverter : DefaultConverter
    {
        protected override bool TryConvert(object? input, Type conversionType, IFormatProvider? provider, out object? value)
        {
            if (input is bool boolean && conversionType == typeof(string))
            {
                value = boolean ? "Yes" : "No";
                return true;
            }

            return base.TryConvert(input, conversionType, provider, out value);
        }
    }
    #endregion

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
