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

internal sealed class InheritedOrderModel
{
    public int A { get; set; }

    [YamlPropertyOrder(-1)]
    public int B { get; set; }

    public int C { get; set; }
}

// The derived type is declared before its base type, so the metadata tokens of its members are smaller.
internal sealed class InheritedDerivedDeclaredFirst : InheritedBaseDeclaredLast
{
    public int Derived { get; set; }

    public override int Overridden { get; set; }

    public int DerivedField;
}

internal class InheritedBaseDeclaredLast
{
    public int BaseField;

    public int Base { get; set; }

    public virtual int Overridden { get; set; }

    public int Other { get; set; }
}

internal class InheritedHidingBase
{
    public int Value { get; set; }
}

internal sealed class InheritedHidingDerived : InheritedHidingBase
{
    public new string? Value { get; set; }
}

internal sealed class InheritedIgnoredHidingDerived : InheritedHidingBase
{
    [YamlIgnore]
    public new string? Value { get; set; }

    public int Other { get; set; }
}

internal class InheritedPrivateIncludeBase
{
    [YamlInclude]
    private int Hidden { get; set; } = 1;

    public int GetHidden() => Hidden;
}

internal sealed class InheritedPrivateIncludeDerived : InheritedPrivateIncludeBase
{
    public int Visible { get; set; }
}

internal class InheritedAttributeBase
{
    [YamlPropertyName("renamed")]
    public virtual int Renamed { get; set; }

    [YamlIgnore]
    public virtual int Ignored { get; set; }

    [YamlRequired]
    public virtual int Required { get; set; }
}

internal sealed class InheritedAttributeDerived : InheritedAttributeBase
{
    public override int Renamed { get; set; }

    public override int Ignored { get; set; }

    public override int Required { get; set; }
}

internal class InheritedNumberHandlingBase
{
    public int BaseValue { get; set; }
}

[YamlNumberHandling(YamlNumberHandling.WriteAsString | YamlNumberHandling.AllowReadingFromString)]
internal sealed class InheritedNumberHandlingDerived : InheritedNumberHandlingBase
{
    public int DerivedValue { get; set; }
}

[YamlSerializable(typeof(GeneratedInheritedJsonNamedDerived))]
[YamlSerializable(typeof(InheritedOrderModel))]
[YamlSerializable(typeof(InheritedDerivedDeclaredFirst))]
[YamlSerializable(typeof(InheritedHidingDerived))]
[YamlSerializable(typeof(InheritedIgnoredHidingDerived))]
[YamlSerializable(typeof(InheritedPrivateIncludeDerived))]
[YamlSerializable(typeof(InheritedAttributeDerived))]
[YamlSerializable(typeof(InheritedNumberHandlingDerived))]
internal sealed partial class InheritedMemberYamlSerializerContext : YamlSerializerContext
{
    public InheritedMemberYamlSerializerContext()
    {
    }

    public InheritedMemberYamlSerializerContext(YamlSerializerOptions options)
        : base(options)
    {
    }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertyOrderAttribute_OrdersMembers(bool useSourceGeneration)
    {
        var yaml = Serialize(new InheritedOrderModel { A = 1, B = 2, C = 3 }, new YamlSerializerOptions(), useSourceGeneration);

        Assert.Equal("B: 2\nA: 1\nC: 3\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortedMappingOrder_SortsMembersByNameAfterPropertyOrder(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { MappingOrder = YamlMappingOrderPolicy.Sorted };

        Assert.Equal("B: 2\nA: 1\nC: 3\n", Serialize(new InheritedOrderModel { A = 1, B = 2, C = 3 }, options, useSourceGeneration));
        Assert.Equal("Base: 2\nDerived: 4\nOther: 3\nOverridden: 5\n", Serialize(new InheritedDerivedDeclaredFirst { Base = 2, Other = 3, Derived = 4, Overridden = 5 }, options, useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclarationOrder_ListsBaseMembersFirstAndPropertiesBeforeFields(bool useSourceGeneration)
    {
        var value = new InheritedDerivedDeclaredFirst { BaseField = 1, Base = 2, Other = 3, Derived = 4, Overridden = 5, DerivedField = 6 };

        var yaml = Serialize(value, new YamlSerializerOptions { IncludeFields = true }, useSourceGeneration);

        Assert.Equal("Base: 2\nOverridden: 5\nOther: 3\nBaseField: 1\nDerived: 4\nDerivedField: 6\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HidingMember_ReplacesHiddenMember(bool useSourceGeneration)
    {
        var yaml = Serialize(new InheritedHidingDerived { Value = "text" }, new YamlSerializerOptions(), useSourceGeneration);
        Assert.Equal("Value: text\n", yaml);

        var result = Deserialize<InheritedHidingDerived>("Value: other\n", new YamlSerializerOptions(), useSourceGeneration)!;
        Assert.Equal("other", result.Value);
        Assert.Equal(0, ((InheritedHidingBase)result).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoredHidingMember_HidesTheHiddenMember(bool useSourceGeneration)
    {
        var value = new InheritedIgnoredHidingDerived { Value = "text", Other = 2 };
        ((InheritedHidingBase)value).Value = 1;

        var yaml = Serialize(value, new YamlSerializerOptions(), useSourceGeneration);
        Assert.Equal("Other: 2\n", yaml);

        var result = Deserialize<InheritedIgnoredHidingDerived>("Value: 5\nOther: 3\n", new YamlSerializerOptions(), useSourceGeneration)!;
        Assert.Null(result.Value);
        Assert.Equal(0, ((InheritedHidingBase)result).Value);
        Assert.Equal(3, result.Other);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateBaseMemberWithYamlInclude_IsSerialized(bool useSourceGeneration)
    {
        var yaml = Serialize(new InheritedPrivateIncludeDerived { Visible = 2 }, new YamlSerializerOptions(), useSourceGeneration);
        Assert.Equal("Hidden: 1\nVisible: 2\n", yaml);

        var options = new YamlSerializerOptions { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow };
        var result = Deserialize<InheritedPrivateIncludeDerived>("Hidden: 5\nVisible: 3\n", options, useSourceGeneration)!;
        Assert.Equal(5, result.GetHidden());
        Assert.Equal(3, result.Visible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverridingMember_InheritsAttributesOfOverriddenMember(bool useSourceGeneration)
    {
        var yaml = Serialize(new InheritedAttributeDerived { Renamed = 1, Ignored = 2, Required = 3 }, new YamlSerializerOptions(), useSourceGeneration);
        Assert.Equal("renamed: 1\nRequired: 3\n", yaml);

        var result = Deserialize<InheritedAttributeDerived>("renamed: 4\nIgnored: 5\nRequired: 6\n", new YamlSerializerOptions(), useSourceGeneration)!;
        Assert.Equal(4, result.Renamed);
        Assert.Equal(0, result.Ignored);
        Assert.Equal(6, result.Required);

        var exception = Assert.Throws<YamlException>(() => Deserialize<InheritedAttributeDerived>("renamed: 4\n", new YamlSerializerOptions(), useSourceGeneration));
        Assert.Contains("Missing required members for", exception.Message);
        Assert.Contains("Required", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeLevelNumberHandling_AppliesToInheritedMembers(bool useSourceGeneration)
    {
        var yaml = Serialize(new InheritedNumberHandlingDerived { BaseValue = 1, DerivedValue = 2 }, new YamlSerializerOptions(), useSourceGeneration);
        Assert.Equal("BaseValue: \"1\"\nDerivedValue: \"2\"\n", yaml);

        var result = Deserialize<InheritedNumberHandlingDerived>("BaseValue: \"3\"\nDerivedValue: \"4\"\n", new YamlSerializerOptions(), useSourceGeneration)!;
        Assert.Equal(3, result.BaseValue);
        Assert.Equal(4, result.DerivedValue);
    }

    private static string Serialize<T>(T value, YamlSerializerOptions options, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, new InheritedMemberYamlSerializerContext(options))
            : YamlSerializer.Serialize(value, options);

    private static T? Deserialize<T>(string yaml, YamlSerializerOptions options, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, new InheritedMemberYamlSerializerContext(options))
            : YamlSerializer.Deserialize<T>(yaml, options);
}
