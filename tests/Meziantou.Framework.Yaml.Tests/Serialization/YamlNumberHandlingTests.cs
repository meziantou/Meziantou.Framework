#if NET11_0_OR_GREATER
using System.Numerics;
#endif
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlNumberHandlingTests
{
    [Fact]
    public void WriteAsString_EmitsQuotedNumber()
    {
        var yaml = YamlSerializer.Serialize(new WriteAsStringModel { Value = 123 });
        Assert.Contains("Value: \"123\"", yaml);
    }

    [Fact]
    public void WriteAsString_RoundTrips()
    {
        var yaml = YamlSerializer.Serialize(new WriteAsStringModel { Value = 123 });
        var roundTrip = YamlSerializer.Deserialize<WriteAsStringModel>(yaml)!;
        Assert.Equal(123, roundTrip.Value);
    }

    [Fact]
    public void AllowNamedFloatingPointLiterals_WritesAndReadsNaN()
    {
        var yaml = YamlSerializer.Serialize(new NamedFloatModel { Value = double.NaN });
        Assert.Contains("Value: \"NaN\"", yaml);

        var roundTrip = YamlSerializer.Deserialize<NamedFloatModel>(yaml)!;
        Assert.True(double.IsNaN(roundTrip.Value));
    }

    [Fact]
    public void AllowNamedFloatingPointLiterals_WritesAndReadsInfinity()
    {
        var yaml = YamlSerializer.Serialize(new NamedFloatModel { Value = double.PositiveInfinity });
        Assert.Contains("Value: \"Infinity\"", yaml);

        var roundTrip = YamlSerializer.Deserialize<NamedFloatModel>(yaml)!;
        Assert.True(double.IsPositiveInfinity(roundTrip.Value));
    }

    [Fact]
    public void TypeLevel_AppliesToAllNumericMembers()
    {
        var yaml = YamlSerializer.Serialize(new TypeLevelModel { First = 1, Second = 2 });
        Assert.Contains("First: \"1\"", yaml);
        Assert.Contains("Second: \"2\"", yaml);
    }

    [Fact]
    public void SourceGenerated_WriteAsString_RoundTrips()
    {
        var yaml = YamlSerializer.Serialize(new WriteAsStringModel { Value = 123 }, NumberHandlingContext.Default);
        Assert.Contains("Value: \"123\"", yaml);

        var roundTrip = YamlSerializer.Deserialize("Value: \"456\"\n", NumberHandlingContext.Default.WriteAsStringModel)!;
        Assert.Equal(456, roundTrip.Value);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void AllowNamedFloatingPointLiterals_Ieee754_WritesAndReadsNaN()
    {
        var yaml = YamlSerializer.Serialize(new NamedIeee754Model
        {
            Brain = BFloat16.NaN,
            Small = Decimal32.NaN,
            Medium = Decimal64.NaN,
            Large = Decimal128.NaN,
            OptionalMedium = Decimal64.NaN,
        });

        Assert.Contains("Brain: \"NaN\"", yaml);
        Assert.Contains("Small: \"NaN\"", yaml);
        Assert.Contains("Medium: \"NaN\"", yaml);
        Assert.Contains("Large: \"NaN\"", yaml);
        Assert.Contains("OptionalMedium: \"NaN\"", yaml);

        var roundTrip = YamlSerializer.Deserialize<NamedIeee754Model>(yaml)!;
        Assert.True(BFloat16.IsNaN(roundTrip.Brain));
        Assert.True(Decimal32.IsNaN(roundTrip.Small));
        Assert.True(Decimal64.IsNaN(roundTrip.Medium));
        Assert.True(Decimal128.IsNaN(roundTrip.Large));
        Assert.True(Decimal64.IsNaN(roundTrip.OptionalMedium!.Value));
    }

    [Fact]
    public void AllowNamedFloatingPointLiterals_Ieee754_WritesAndReadsInfinity()
    {
        var yaml = YamlSerializer.Serialize(new NamedIeee754Model
        {
            Brain = BFloat16.PositiveInfinity,
            Small = Decimal32.NegativeInfinity,
            Medium = Decimal64.PositiveInfinity,
            Large = Decimal128.NegativeInfinity,
            OptionalMedium = Decimal64.NegativeInfinity,
        });

        Assert.Contains("Brain: \"Infinity\"", yaml);
        Assert.Contains("Small: \"-Infinity\"", yaml);
        Assert.Contains("Medium: \"Infinity\"", yaml);
        Assert.Contains("Large: \"-Infinity\"", yaml);
        Assert.Contains("OptionalMedium: \"-Infinity\"", yaml);

        var roundTrip = YamlSerializer.Deserialize<NamedIeee754Model>(yaml)!;
        Assert.Equal(BFloat16.PositiveInfinity, roundTrip.Brain);
        Assert.Equal(Decimal32.NegativeInfinity, roundTrip.Small);
        Assert.Equal(Decimal64.PositiveInfinity, roundTrip.Medium);
        Assert.Equal(Decimal128.NegativeInfinity, roundTrip.Large);
        Assert.Equal(Decimal64.NegativeInfinity, roundTrip.OptionalMedium);
    }

    [Fact]
    public void AllowNamedFloatingPointLiterals_Ieee754_ReadsPlusInfinity()
    {
        var roundTrip = YamlSerializer.Deserialize<NamedIeee754Model>("Medium: \"+Infinity\"\n")!;
        Assert.Equal(Decimal64.PositiveInfinity, roundTrip.Medium);
    }

    [Fact]
    public void AllowNamedFloatingPointLiterals_Ieee754_FiniteValuesUseYamlNumbers()
    {
        var yaml = YamlSerializer.Serialize(new NamedIeee754Model
        {
            Brain = (BFloat16)1.5f,
            Small = Decimal32.Parse("-5.30", CultureInfo.InvariantCulture),
            Medium = Decimal64.Parse("123.456", CultureInfo.InvariantCulture),
            Large = Decimal128.Parse("1.25", CultureInfo.InvariantCulture),
            OptionalMedium = null,
        });

        Assert.Contains("Brain: 1.5", yaml);
        Assert.Contains("Small: -5.30", yaml);
        Assert.Contains("Medium: 123.456", yaml);
        Assert.Contains("Large: 1.25", yaml);
        Assert.Contains("OptionalMedium: null", yaml);
    }

    [Fact]
    public void WriteAsString_Ieee754_RoundTrips()
    {
        var yaml = YamlSerializer.Serialize(new WriteAsStringIeee754Model { Value = Decimal64.Parse("123.456", CultureInfo.InvariantCulture) });
        Assert.Contains("Value: \"123.456\"", yaml);

        var roundTrip = YamlSerializer.Deserialize<WriteAsStringIeee754Model>(yaml)!;
        Assert.Equal(Decimal64.Parse("123.456", CultureInfo.InvariantCulture), roundTrip.Value);
    }

    [Fact]
    public void SourceGenerated_AllowNamedFloatingPointLiterals_Ieee754_RoundTrips()
    {
        var payload = new NamedIeee754Model
        {
            Brain = BFloat16.NaN,
            Small = Decimal32.PositiveInfinity,
            Medium = Decimal64.NegativeInfinity,
            Large = Decimal128.NaN,
            OptionalMedium = Decimal64.PositiveInfinity,
        };

        var yaml = YamlSerializer.Serialize(payload, NumberHandlingContext.Default.NamedIeee754Model);
        Assert.Contains("Brain: \"NaN\"", yaml);
        Assert.Contains("Small: \"Infinity\"", yaml);
        Assert.Contains("Medium: \"-Infinity\"", yaml);
        Assert.Contains("Large: \"NaN\"", yaml);
        Assert.Contains("OptionalMedium: \"Infinity\"", yaml);

        var roundTrip = YamlSerializer.Deserialize(yaml, NumberHandlingContext.Default.NamedIeee754Model)!;
        Assert.True(BFloat16.IsNaN(roundTrip.Brain));
        Assert.Equal(Decimal32.PositiveInfinity, roundTrip.Small);
        Assert.Equal(Decimal64.NegativeInfinity, roundTrip.Medium);
        Assert.True(Decimal128.IsNaN(roundTrip.Large));
        Assert.Equal(Decimal64.PositiveInfinity, roundTrip.OptionalMedium);
    }
#endif
}

#pragma warning disable MA0048 // File name must match type name
internal sealed class WriteAsStringModel
{
    [YamlNumberHandling(YamlNumberHandling.WriteAsString | YamlNumberHandling.AllowReadingFromString)]
    public int Value { get; set; }
}

internal sealed class NamedFloatModel
{
    [YamlNumberHandling(YamlNumberHandling.AllowNamedFloatingPointLiterals)]
    public double Value { get; set; }
}

[YamlNumberHandling(YamlNumberHandling.WriteAsString)]
internal sealed class TypeLevelModel
{
    public int First { get; set; }
    public int Second { get; set; }
}

#if NET11_0_OR_GREATER
[YamlNumberHandling(YamlNumberHandling.AllowNamedFloatingPointLiterals)]
internal sealed class NamedIeee754Model
{
    public BFloat16 Brain { get; set; }
    public Decimal32 Small { get; set; }
    public Decimal64 Medium { get; set; }
    public Decimal128 Large { get; set; }
    public Decimal64? OptionalMedium { get; set; }
}

internal sealed class WriteAsStringIeee754Model
{
    [YamlNumberHandling(YamlNumberHandling.WriteAsString | YamlNumberHandling.AllowReadingFromString)]
    public Decimal64 Value { get; set; }
}
#endif

[YamlSerializable(typeof(WriteAsStringModel))]
#if NET11_0_OR_GREATER
[YamlSerializable(typeof(NamedIeee754Model))]
#endif
internal sealed partial class NumberHandlingContext : YamlSerializerContext
{
    public NumberHandlingContext()
    {
    }

    public NumberHandlingContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
