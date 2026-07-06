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

[YamlSerializable(typeof(WriteAsStringModel))]
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
