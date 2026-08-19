using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlNamingPolicyAttributeTests
{
    [Fact]
    public void MemberLevel_AppliesPolicyToAnnotatedMemberOnly()
    {
        var yaml = YamlSerializer.Serialize(new MemberPolicyModel { FirstValue = 1, SecondValue = 2 });
        Assert.Contains("firstValue: 1", yaml);
        Assert.Contains("SecondValue: 2", yaml);
    }

    [Fact]
    public void MemberLevel_RoundTrips()
    {
        var roundTrip = YamlSerializer.Deserialize<MemberPolicyModel>("firstValue: 1\nSecondValue: 2\n")!;
        Assert.Equal(1, roundTrip.FirstValue);
        Assert.Equal(2, roundTrip.SecondValue);
    }

    [Fact]
    public void TypeLevel_AppliesPolicyToAllMembers()
    {
        var yaml = YamlSerializer.Serialize(new TypePolicyModel { FirstValue = 1, SecondValue = 2 });
        Assert.Contains("first_value: 1", yaml);
        Assert.Contains("second_value: 2", yaml);
    }

    [Fact]
    public void MemberLevel_OverridesTypeLevel()
    {
        var yaml = YamlSerializer.Serialize(new TypeAndMemberPolicyModel { FirstValue = 1, SecondValue = 2 });
        Assert.Contains("first-value: 1", yaml);
        Assert.Contains("second_value: 2", yaml);
    }

    [Fact]
    public void PropertyNameAttribute_TakesPrecedence()
    {
        var yaml = YamlSerializer.Serialize(new ExplicitNameModel { FirstValue = 1 });
        Assert.Contains("explicit: 1", yaml);
    }

    [Fact]
    public void Attribute_OverridesSerializerOptions()
    {
        var options = new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseUpper };
        var yaml = YamlSerializer.Serialize(new TypePolicyModel { FirstValue = 1, SecondValue = 2 }, options);
        Assert.Contains("first_value: 1", yaml);
        Assert.Contains("second_value: 2", yaml);
    }

    [Fact]
    public void Unspecified_OptsOutOfSerializerOptions()
    {
        var options = new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower };
        var yaml = YamlSerializer.Serialize(new UnspecifiedPolicyModel { FirstValue = 1, SecondValue = 2 }, options);
        Assert.Contains("FirstValue: 1", yaml);
        Assert.Contains("second_value: 2", yaml);
    }

    [Fact]
    public void TypeLevel_AppliesToInheritedMembers()
    {
        var yaml = YamlSerializer.Serialize(new DerivedPolicyModel { BaseValue = 1, DerivedValue = 2 });
        Assert.Contains("base_value: 1", yaml);
        Assert.Contains("derived_value: 2", yaml);
    }

    [Fact]
    public void TypeLevel_AppliesToConstructorParameters()
    {
        var roundTrip = YamlSerializer.Deserialize<ConstructorPolicyModel>("first_value: 1\nsecond_value: 2\n")!;
        Assert.Equal(1, roundTrip.FirstValue);
        Assert.Equal(2, roundTrip.SecondValue);
    }

    [Fact]
    public void MemberLevel_Unspecified_OptsOutOfTypeLevel()
    {
        var yaml = YamlSerializer.Serialize(new TypeLevelOptOutModel { FirstValue = 1, SecondValue = 2 });
        Assert.Contains("FirstValue: 1", yaml);
        Assert.Contains("second_value: 2", yaml);
    }

    [Fact]
    public void TypeLevel_PascalCase_UppercasesFirstCharacter()
    {
        var yaml = YamlSerializer.Serialize(new PascalCasePolicyModel { firstValue = 1, secondValue = 2 });
        Assert.Contains("FirstValue: 1", yaml);
        Assert.Contains("SecondValue: 2", yaml);

        var roundTrip = YamlSerializer.Deserialize<PascalCasePolicyModel>("FirstValue: 3\nSecondValue: 4\n")!;
        Assert.Equal(3, roundTrip.firstValue);
        Assert.Equal(4, roundTrip.secondValue);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_PascalCase_UppercasesFirstCharacter()
    {
        var yaml = YamlSerializer.Serialize(new PascalCasePolicyModel { firstValue = 1, secondValue = 2 }, NamingPolicyContext.Default);
        Assert.Contains("FirstValue: 1", yaml);
        Assert.Contains("SecondValue: 2", yaml);

        var roundTrip = YamlSerializer.Deserialize("FirstValue: 3\nSecondValue: 4\n", NamingPolicyContext.Default.PascalCasePolicyModel)!;
        Assert.Equal(3, roundTrip.firstValue);
        Assert.Equal(4, roundTrip.secondValue);
    }

    [Fact]
    public void SourceGenerated_MemberLevel_Unspecified_OptsOutOfTypeLevel()
    {
        var yaml = YamlSerializer.Serialize(new TypeLevelOptOutModel { FirstValue = 1, SecondValue = 2 }, NamingPolicyContext.Default);
        Assert.Contains("FirstValue: 1", yaml);
        Assert.Contains("second_value: 2", yaml);
    }

    [Fact]
    public void SourceGenerated_PropertyNameAttribute_TakesPrecedence()
    {
        var yaml = YamlSerializer.Serialize(new ExplicitNameModel { FirstValue = 1 }, NamingPolicyContext.Default);
        Assert.Contains("explicit: 1", yaml);
    }

    [Fact]
    public void SourceGenerated_MemberLevel_RoundTrips()
    {
        var yaml = YamlSerializer.Serialize(new MemberPolicyModel { FirstValue = 1, SecondValue = 2 }, NamingPolicyContext.Default);
        Assert.Contains("firstValue: 1", yaml);
        Assert.Contains("SecondValue: 2", yaml);

        var roundTrip = YamlSerializer.Deserialize("firstValue: 3\nSecondValue: 4\n", NamingPolicyContext.Default.MemberPolicyModel)!;
        Assert.Equal(3, roundTrip.FirstValue);
        Assert.Equal(4, roundTrip.SecondValue);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_RoundTrips()
    {
        var yaml = YamlSerializer.Serialize(new TypePolicyModel { FirstValue = 1, SecondValue = 2 }, NamingPolicyContext.Default);
        Assert.Contains("first_value: 1", yaml);
        Assert.Contains("second_value: 2", yaml);

        var roundTrip = YamlSerializer.Deserialize("first_value: 3\nsecond_value: 4\n", NamingPolicyContext.Default.TypePolicyModel)!;
        Assert.Equal(3, roundTrip.FirstValue);
        Assert.Equal(4, roundTrip.SecondValue);
    }

    [Fact]
    public void SourceGenerated_MemberLevel_OverridesTypeLevel()
    {
        var yaml = YamlSerializer.Serialize(new TypeAndMemberPolicyModel { FirstValue = 1, SecondValue = 2 }, NamingPolicyContext.Default);
        Assert.Contains("first-value: 1", yaml);
        Assert.Contains("second_value: 2", yaml);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_AppliesToInheritedMembers()
    {
        var yaml = YamlSerializer.Serialize(new DerivedPolicyModel { BaseValue = 1, DerivedValue = 2 }, NamingPolicyContext.Default);
        Assert.Contains("base_value: 1", yaml);
        Assert.Contains("derived_value: 2", yaml);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_AppliesToConstructorParameters()
    {
        var roundTrip = YamlSerializer.Deserialize("first_value: 1\nsecond_value: 2\n", NamingPolicyContext.Default.ConstructorPolicyModel)!;
        Assert.Equal(1, roundTrip.FirstValue);
        Assert.Equal(2, roundTrip.SecondValue);
    }
}

#pragma warning disable MA0048 // File name must match type name
internal sealed class MemberPolicyModel
{
    [YamlNamingPolicy(YamlKnownNamingPolicy.CamelCase)]
    public int FirstValue { get; set; }

    public int SecondValue { get; set; }
}

[YamlNamingPolicy(YamlKnownNamingPolicy.SnakeCaseLower)]
internal sealed class TypePolicyModel
{
    public int FirstValue { get; set; }
    public int SecondValue { get; set; }
}

[YamlNamingPolicy(YamlKnownNamingPolicy.SnakeCaseLower)]
internal sealed class TypeAndMemberPolicyModel
{
    [YamlNamingPolicy(YamlKnownNamingPolicy.KebabCaseLower)]
    public int FirstValue { get; set; }

    public int SecondValue { get; set; }
}

[YamlNamingPolicy(YamlKnownNamingPolicy.SnakeCaseLower)]
internal sealed class ExplicitNameModel
{
    [YamlPropertyName("explicit")]
    [YamlNamingPolicy(YamlKnownNamingPolicy.CamelCase)]
    public int FirstValue { get; set; }
}

[YamlNamingPolicy(YamlKnownNamingPolicy.SnakeCaseLower)]
internal sealed class TypeLevelOptOutModel
{
    [YamlNamingPolicy(YamlKnownNamingPolicy.Unspecified)]
    public int FirstValue { get; set; }

    public int SecondValue { get; set; }
}

internal sealed class UnspecifiedPolicyModel
{
    [YamlNamingPolicy(YamlKnownNamingPolicy.Unspecified)]
    public int FirstValue { get; set; }

    public int SecondValue { get; set; }
}

#pragma warning disable IDE1006 // Naming Styles - the members must start with a lowercase character for the PascalCase policy to have an effect
[YamlNamingPolicy(YamlKnownNamingPolicy.PascalCase)]
internal sealed class PascalCasePolicyModel
{
    public int firstValue { get; set; }
    public int secondValue { get; set; }
}
#pragma warning restore IDE1006 // Naming Styles

[YamlNamingPolicy(YamlKnownNamingPolicy.SnakeCaseLower)]
internal class BasePolicyModel
{
    public int BaseValue { get; set; }
}

internal sealed class DerivedPolicyModel : BasePolicyModel
{
    public int DerivedValue { get; set; }
}

[YamlNamingPolicy(YamlKnownNamingPolicy.SnakeCaseLower)]
internal sealed class ConstructorPolicyModel
{
    public ConstructorPolicyModel(int firstValue, int secondValue)
    {
        FirstValue = firstValue;
        SecondValue = secondValue;
    }

    public int FirstValue { get; }
    public int SecondValue { get; }
}

[YamlSerializable(typeof(MemberPolicyModel))]
[YamlSerializable(typeof(TypePolicyModel))]
[YamlSerializable(typeof(TypeAndMemberPolicyModel))]
[YamlSerializable(typeof(TypeLevelOptOutModel))]
[YamlSerializable(typeof(ExplicitNameModel))]
[YamlSerializable(typeof(DerivedPolicyModel))]
[YamlSerializable(typeof(ConstructorPolicyModel))]
[YamlSerializable(typeof(PascalCasePolicyModel))]
internal sealed partial class NamingPolicyContext : YamlSerializerContext
{
    public NamingPolicyContext()
    {
    }

    public NamingPolicyContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
