#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

internal abstract class GeneratedInheritedJsonNamedBase
{
    [YamlPropertyName("base_value")]
    public string? BaseValue { get; init; }
}

internal sealed class GeneratedInheritedJsonNamedDerived : GeneratedInheritedJsonNamedBase
{
    [YamlPropertyName("derived_value")]
    public string? DerivedValue { get; init; }
}

[YamlSerializable(typeof(GeneratedInheritedJsonNamedDerived))]
internal sealed partial class InheritedMemberYamlSerializerContext : YamlSerializerContext
{
}
public class YamlInheritedMemberSourceGenerationTests
{
    [Fact]
    public void ReflectionDeserializerIncludesJsonNamedBaseMembers()
    {
        var yaml = "base_value: base\nderived_value: derived\n";

        var result = YamlSerializer.Deserialize<GeneratedInheritedJsonNamedDerived>(yaml);

        Assert.NotNull(result);
        Assert.Equal("base", result.BaseValue);
        Assert.Equal("derived", result.DerivedValue);
    }

    [Fact]
    public void ReflectionSerializerIncludesJsonNamedBaseMembers()
    {
        var yaml = YamlSerializer.Serialize(new GeneratedInheritedJsonNamedDerived
        {
            BaseValue = "base",
            DerivedValue = "derived",
        });

        Assert.Contains("base_value: base", yaml);
        Assert.Contains("derived_value: derived", yaml);
    }

    [Fact]
    public void SourceGeneratedDeserializerIncludesJsonNamedBaseMembers()
    {
        var yaml = "base_value: base\nderived_value: derived\n";
        var context = InheritedMemberYamlSerializerContext.Default;

        var result = YamlSerializer.Deserialize(yaml, context.GeneratedInheritedJsonNamedDerived);

        Assert.NotNull(result);
        Assert.Equal("base", result.BaseValue);
        Assert.Equal("derived", result.DerivedValue);
    }

    [Fact]
    public void SourceGeneratedSerializerIncludesJsonNamedBaseMembers()
    {
        var context = InheritedMemberYamlSerializerContext.Default;
        var yaml = YamlSerializer.Serialize(new GeneratedInheritedJsonNamedDerived
        {
            BaseValue = "base",
            DerivedValue = "derived",
        }, context.GeneratedInheritedJsonNamedDerived);

        Assert.Contains("base_value: base", yaml);
        Assert.Contains("derived_value: derived", yaml);
    }
}
