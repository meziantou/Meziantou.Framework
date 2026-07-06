using Meziantou.Framework.Yaml.Serialization;

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

[YamlSerializable(typeof(ColorModel))]
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
