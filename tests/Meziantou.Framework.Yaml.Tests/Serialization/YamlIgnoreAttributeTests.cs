using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlIgnoreAttributeTests
{
    [Fact]
    public void TypeLevel_AppliesConditionToAllMembers()
    {
        var yaml = YamlSerializer.Serialize(new TypeIgnoreModel { FirstValue = null, SecondValue = "b" });
        Assert.DoesNotContain("FirstValue", yaml);
        Assert.Contains("SecondValue: b", yaml);
    }

    [Fact]
    public void MemberLevel_OverridesTypeLevel()
    {
        var yaml = YamlSerializer.Serialize(new TypeAndMemberIgnoreModel { FirstValue = null, SecondValue = null });
        Assert.Contains("FirstValue:", yaml);
        Assert.DoesNotContain("SecondValue", yaml);
    }

    [Fact]
    public void TypeLevel_OverridesSerializerOptions()
    {
        var options = new YamlSerializerOptions { DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingDefault };
        var yaml = YamlSerializer.Serialize(new TypeNeverIgnoreModel { FirstValue = 0 }, options);
        Assert.Contains("FirstValue: 0", yaml);
    }

    [Fact]
    public void TypeLevel_Always_IgnoresAllMembers()
    {
        var yaml = YamlSerializer.Serialize(new TypeAlwaysIgnoreModel { FirstValue = 1, SecondValue = 2 });
        Assert.DoesNotContain("FirstValue", yaml);
        Assert.Contains("SecondValue: 2", yaml);

        var roundTrip = YamlSerializer.Deserialize<TypeAlwaysIgnoreModel>("FirstValue: 1\nSecondValue: 2\n")!;
        Assert.Equal(0, roundTrip.FirstValue);
        Assert.Equal(2, roundTrip.SecondValue);
    }

    [Fact]
    public void TypeLevel_AppliesToFields()
    {
        var options = new YamlSerializerOptions { IncludeFields = true };
        var yaml = YamlSerializer.Serialize(new TypeIgnoreFieldModel { FirstValue = null, SecondValue = "b" }, options);
        Assert.DoesNotContain("FirstValue", yaml);
        Assert.Contains("SecondValue: b", yaml);
    }

    [Fact]
    public void TypeLevel_AppliesToInheritedMembers()
    {
        var yaml = YamlSerializer.Serialize(new DerivedIgnoreModel { BaseValue = null, DerivedValue = null });
        Assert.Equal("{}", yaml.Trim(), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void TypeLevel_WhenReading_IsIgnoredOnDeserialization()
    {
        var yaml = YamlSerializer.Serialize(new TypeWhenReadingIgnoreModel { FirstValue = 1 });
        Assert.Contains("FirstValue: 1", yaml);

        var roundTrip = YamlSerializer.Deserialize<TypeWhenReadingIgnoreModel>("FirstValue: 42\n")!;
        Assert.Equal(0, roundTrip.FirstValue);
    }

    [Fact]
    public void TypeLevel_DoesNotIgnoreExtensionData()
    {
        var yaml = YamlSerializer.Serialize(new TypeAlwaysIgnoreExtensionDataModel
        {
            FirstValue = 1,
            ExtensionData = new Dictionary<string, object?>(StringComparer.Ordinal) { ["extra"] = "value" },
        });

        Assert.DoesNotContain("FirstValue", yaml);
        Assert.Contains("extra: value", yaml);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_AppliesConditionToAllMembers()
    {
        var yaml = YamlSerializer.Serialize(new TypeIgnoreModel { FirstValue = null, SecondValue = "b" }, IgnoreConditionContext.Default);
        Assert.DoesNotContain("FirstValue", yaml);
        Assert.Contains("SecondValue: b", yaml);
    }

    [Fact]
    public void SourceGenerated_MemberLevel_OverridesTypeLevel()
    {
        var yaml = YamlSerializer.Serialize(new TypeAndMemberIgnoreModel { FirstValue = null, SecondValue = null }, IgnoreConditionContext.Default);
        Assert.Contains("FirstValue:", yaml);
        Assert.DoesNotContain("SecondValue", yaml);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_OverridesSerializerOptions()
    {
        var context = new IgnoreConditionContext(new YamlSerializerOptions { DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingDefault });
        var yaml = YamlSerializer.Serialize(new TypeNeverIgnoreModel { FirstValue = 0 }, context.TypeNeverIgnoreModel);
        Assert.Contains("FirstValue: 0", yaml);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_Always_IgnoresAllMembers()
    {
        var yaml = YamlSerializer.Serialize(new TypeAlwaysIgnoreModel { FirstValue = 1, SecondValue = 2 }, IgnoreConditionContext.Default);
        Assert.DoesNotContain("FirstValue", yaml);
        Assert.Contains("SecondValue: 2", yaml);

        var roundTrip = YamlSerializer.Deserialize("FirstValue: 1\nSecondValue: 2\n", IgnoreConditionContext.Default.TypeAlwaysIgnoreModel)!;
        Assert.Equal(0, roundTrip.FirstValue);
        Assert.Equal(2, roundTrip.SecondValue);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_AppliesToInheritedMembers()
    {
        var yaml = YamlSerializer.Serialize(new DerivedIgnoreModel { BaseValue = null, DerivedValue = null }, IgnoreConditionContext.Default);
        Assert.Equal("{}", yaml.Trim(), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SourceGenerated_TypeLevel_WhenReading_IsIgnoredOnDeserialization()
    {
        var yaml = YamlSerializer.Serialize(new TypeWhenReadingIgnoreModel { FirstValue = 1 }, IgnoreConditionContext.Default);
        Assert.Contains("FirstValue: 1", yaml);

        var roundTrip = YamlSerializer.Deserialize("FirstValue: 42\n", IgnoreConditionContext.Default.TypeWhenReadingIgnoreModel)!;
        Assert.Equal(0, roundTrip.FirstValue);
    }
}

#pragma warning disable MA0048 // File name must match type name
[YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingNull)]
internal sealed class TypeIgnoreModel
{
    public string? FirstValue { get; set; }
    public string? SecondValue { get; set; }
}

[YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingNull)]
internal sealed class TypeAndMemberIgnoreModel
{
    [YamlIgnore(Condition = YamlIgnoreCondition.Never)]
    public string? FirstValue { get; set; }

    public string? SecondValue { get; set; }
}

[YamlIgnore(Condition = YamlIgnoreCondition.Never)]
internal sealed class TypeNeverIgnoreModel
{
    public int FirstValue { get; set; }
}

[YamlIgnore]
internal sealed class TypeAlwaysIgnoreModel
{
    public int FirstValue { get; set; }

    [YamlIgnore(Condition = YamlIgnoreCondition.Never)]
    public int SecondValue { get; set; }
}

[YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingNull)]
internal sealed class TypeIgnoreFieldModel
{
    public string? FirstValue;
    public string? SecondValue;
}

[YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingNull)]
internal class BaseIgnoreModel
{
    public string? BaseValue { get; set; }
}

internal sealed class DerivedIgnoreModel : BaseIgnoreModel
{
    public string? DerivedValue { get; set; }
}

[YamlIgnore(Condition = YamlIgnoreCondition.WhenReading)]
internal sealed class TypeWhenReadingIgnoreModel
{
    public int FirstValue { get; set; }
}

[YamlIgnore]
internal sealed class TypeAlwaysIgnoreExtensionDataModel
{
    public int FirstValue { get; set; }

    [YamlExtensionData]
    public Dictionary<string, object?>? ExtensionData { get; set; }
}

[YamlSerializable(typeof(TypeIgnoreModel))]
[YamlSerializable(typeof(TypeAndMemberIgnoreModel))]
[YamlSerializable(typeof(TypeNeverIgnoreModel))]
[YamlSerializable(typeof(TypeAlwaysIgnoreModel))]
[YamlSerializable(typeof(DerivedIgnoreModel))]
[YamlSerializable(typeof(TypeWhenReadingIgnoreModel))]
internal sealed partial class IgnoreConditionContext : YamlSerializerContext
{
    public IgnoreConditionContext()
    {
    }

    public IgnoreConditionContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
