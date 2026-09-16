using Meziantou.Framework.Yaml.Serialization;
using Meziantou.Xunit;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlEnumMemberNameTests
{
    [Fact]
    public void Serialize_UsesCustomEnumName()
    {
        var yaml = YamlSerializer.Serialize(new ColorModel { Color = Color.Green });
        Assert.Contains("Color: forest-green", yaml);
    }

    [Fact]
    public void Serialize_UnmappedMember_UsesDefaultName()
    {
        var yaml = YamlSerializer.Serialize(new ColorModel { Color = Color.Red });
        Assert.Contains("Color: Red", yaml);
    }

    [Fact]
    public void Deserialize_ReadsCustomEnumName()
    {
        var value = YamlSerializer.Deserialize<ColorModel>("Color: forest-green\n")!;
        Assert.Equal(Color.Green, value.Color);
    }

    [Fact]
    public void Deserialize_StillReadsDefaultName()
    {
        var value = YamlSerializer.Deserialize<ColorModel>("Color: Blue\n")!;
        Assert.Equal(Color.Blue, value.Color);
    }

    [Fact]
    public void SourceGenerated_RoundTripsCustomEnumName()
    {
        var yaml = YamlSerializer.Serialize(new ColorModel { Color = Color.Green }, EnumMemberNameContext.Default);
        Assert.Contains("Color: forest-green", yaml);

        var roundTrip = YamlSerializer.Deserialize("Color: forest-green\n", EnumMemberNameContext.Default.ColorModel)!;
        Assert.Equal(Color.Green, roundTrip.Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomEnumName_AppliesToCollectionsNullablesAndKeys(bool useSourceGeneration)
    {
        var model = new ColorCollectionsModel
        {
            List = [Color.Green, Color.Red],
            Array = [Color.Green],
            Values = new Dictionary<string, Color>(StringComparer.Ordinal) { ["a"] = Color.Green },
            Keys = new Dictionary<Color, int> { [Color.Green] = 1 },
            Nullable = Color.Green,
            NullableList = [Color.Green, null],
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<ColorCollectionsModel>(yaml, useSourceGeneration)!;

        Assert.Equal("""
            List:
              - forest-green
              - Red
            Array:
              - forest-green
            Values:
              a: forest-green
            Keys:
              forest-green: 1
            Nullable: forest-green
            NullableList:
              - forest-green
              - null

            """, yaml, ignoreLineEndingDifferences: true);
        Assert.Equal(model.List, roundTrip.List!);
        Assert.Equal(model.Array, roundTrip.Array!);
        Assert.Equal(model.Values, roundTrip.Values!);
        Assert.Equal(model.Keys, roundTrip.Keys!);
        Assert.Equal(Color.Green, roundTrip.Nullable);
        Assert.Equal(model.NullableList, roundTrip.NullableList!);
        Assert.Equal("forest-green\n", Serialize<Color?>(Color.Green, useSourceGeneration), ignoreLineEndingDifferences: true);
        Assert.Equal(Color.Green, Deserialize<Color?>("forest-green\n", useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomEnumName_IsReadIgnoringCase(bool useSourceGeneration)
    {
        Assert.Equal(Color.Green, Deserialize<Color>("FOREST-GREEN\n", useSourceGeneration));
        Assert.Equal(Color.Green, Deserialize<ColorModel>("Color: Forest-Green\n", useSourceGeneration)!.Color);
        Assert.Equal([Color.Green], Deserialize<List<Color>>("- FOREST-GREEN\n", useSourceGeneration)!);
    }

    [Theory, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData(false)]
    [InlineData(true)]
    public void UndefinedNegativeEnumValue_IsWrittenWithInvariantCulture(bool useSourceGeneration)
    {
        var currentCulture = CultureInfo.CurrentCulture;
        try
        {
            // The negative sign of ar-SA starts with a directional mark.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var model = new ColorCollectionsModel { List = [(Color)(-1)], Keys = new Dictionary<Color, int> { [(Color)(-2)] = 1 } };

            var yaml = Serialize(model, useSourceGeneration);

            Assert.Contains("List:\n  - -1\n", yaml);
            Assert.Contains("Keys:\n  -2: 1\n", yaml);
            Assert.Equal("-1\n", Serialize((Color)(-1), useSourceGeneration), ignoreLineEndingDifferences: true);
        }
        finally
        {
            CultureInfo.CurrentCulture = currentCulture;
        }
    }

    private static string Serialize<T>(T value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, EnumMemberNameContext.Default)
            : YamlSerializer.Serialize(value);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, EnumMemberNameContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);
}

#pragma warning disable MA0048 // File name must match type name
internal enum Color
{
    Red,
    [YamlEnumMemberName("forest-green")]
    Green,
    Blue,
}

internal sealed class ColorModel
{
    public Color Color { get; set; }
}

internal sealed class ColorCollectionsModel
{
    public List<Color>? List { get; set; }

    public Color[]? Array { get; set; }

    public Dictionary<string, Color>? Values { get; set; }

    public Dictionary<Color, int>? Keys { get; set; }

    public Color? Nullable { get; set; }

    public List<Color?>? NullableList { get; set; }
}

[YamlSerializable(typeof(ColorModel))]
[YamlSerializable(typeof(ColorCollectionsModel))]
[YamlSerializable(typeof(Color))]
[YamlSerializable(typeof(Color?))]
[YamlSerializable(typeof(List<Color>))]
internal sealed partial class EnumMemberNameContext : YamlSerializerContext
{
    public EnumMemberNameContext()
    {
    }

    public EnumMemberNameContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
