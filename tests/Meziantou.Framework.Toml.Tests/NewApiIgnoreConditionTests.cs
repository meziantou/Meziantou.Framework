using System.Collections.Generic;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name

public sealed class IgnoreConditionModel
{
    [TomlIgnore(Condition = TomlIgnoreCondition.WhenWritingNull)]
    public string? Optional { get; set; }

    [TomlIgnore]
    public string AlwaysIgnored { get; set; } = "ignored";

    [TomlIgnore(Condition = TomlIgnoreCondition.WhenWritingDefault)]
    public long DefaultIgnored { get; set; }
}

public sealed class DirectionalIgnoreConditionModel
{
    public int Keep { get; set; }

    [TomlIgnore(Condition = TomlIgnoreCondition.Never)]
    public int Never { get; set; }

    [TomlIgnore(Condition = TomlIgnoreCondition.WhenWriting)]
    public int TomlWriteOnly { get; set; }

    [TomlIgnore(Condition = TomlIgnoreCondition.WhenReading)]
    public int TomlReadOnly { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(IgnoreConditionModel))]
[TomlSerializable(typeof(DirectionalIgnoreConditionModel))]
internal sealed partial class TestTomlIgnoreContext : TomlSerializerContext
{
}

public sealed class NeverIgnoreConditionModel
{
    [TomlIgnore(Condition = TomlIgnoreCondition.Never)]
    public int TomlNever { get; set; }

    public int Other { get; set; }
}

[TomlSourceGenerationOptions(DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingDefault)]
[TomlSerializable(typeof(NeverIgnoreConditionModel))]
internal sealed partial class TestTomlNeverIgnoreContext : TomlSerializerContext
{
}

public sealed class NullNeverIgnoredModel
{
    [TomlIgnore(Condition = TomlIgnoreCondition.Never)]
    public string? Name { get; set; } = "a";

    [TomlIgnore(Condition = TomlIgnoreCondition.Never)]
    public IList<int>? Items { get; set; } = [1];

    [TomlIgnore(Condition = TomlIgnoreCondition.Never)]
    public int? Count { get; set; } = 1;
}

public sealed class NonNullableSkippedWhenNullModel
{
    [TomlIgnore(Condition = TomlIgnoreCondition.WhenWritingNull)]
    public string Name { get; set; } = null!;

    [TomlIgnore(Condition = TomlIgnoreCondition.WhenWritingDefault)]
    public string Other { get; set; } = null!;

    public string Required { get; set; } = "r";
}

[TomlSerializable(typeof(NonNullableSkippedWhenNullModel))]
[TomlSerializable(typeof(NullNeverIgnoredModel))]
internal sealed partial class TestTomlNullNeverIgnoredContext : TomlSerializerContext
{
}

public class NewApiIgnoreConditionTests
{
    [Fact]
    public void NonNullableMemberWithItsOwnWhenWritingNull_IsSkippedWhenNull()
    {
        var value = new NonNullableSkippedWhenNullModel();

        Assert.Equal("Required = \"r\"\n", TomlSerializer.Serialize(value));
        Assert.Equal("Required = \"r\"\n", TomlSerializer.Serialize(value, TestTomlNullNeverIgnoredContext.Default.NonNullableSkippedWhenNullModel));

        value.Required = null!;
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(value));
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(value, TestTomlNullNeverIgnoredContext.Default.NonNullableSkippedWhenNullModel));
    }

    [Fact]
    public void NullCollectionElementOrDictionaryValue_ThrowsATomlExceptionNamingIt()
    {
        var list = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new Dictionary<string, List<string?>> { ["a"] = ["x", null] }));
        var set = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new Dictionary<string, HashSet<string?>> { ["a"] = [null] }));
        var dictionary = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new Dictionary<string, string?> { ["key"] = null }));

        Assert.Contains("The element at index 1 of 'System.Collections.Generic.List`1", list.Message, StringComparison.Ordinal);
        Assert.Contains("An element of 'System.Collections.Generic.HashSet`1", set.Message, StringComparison.Ordinal);
        Assert.Contains("The value of the key 'key' in 'System.Collections.Generic.Dictionary`2", dictionary.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(NullNeverIgnoredModel.Name))]
    [InlineData(nameof(NullNeverIgnoredModel.Items))]
    [InlineData(nameof(NullNeverIgnoredModel.Count))]
    public void NullMemberWrittenWithNever_ThrowsATomlExceptionNamingTheMember(string memberName)
    {
        var value = new NullNeverIgnoredModel();
        switch (memberName)
        {
            case nameof(NullNeverIgnoredModel.Name):
                value.Name = null;
                break;
            case nameof(NullNeverIgnoredModel.Items):
                value.Items = null;
                break;
            default:
                value.Count = null;
                break;
        }

        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(value));
        var generated = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(value, TestTomlNullNeverIgnoredContext.Default.NullNeverIgnoredModel));

        Assert.Contains($"'{memberName}'", reflection.Message);
        Assert.Contains("is null", reflection.Message);
        Assert.Equal(reflection.Message, generated.Message);
    }

    [Fact]
    public void GeneratedContext_RespectsIgnoreConditions()
    {
        var context = TestTomlIgnoreContext.Default;

        var empty = new IgnoreConditionModel
        {
            Optional = null,
            AlwaysIgnored = "should_not_appear",
            DefaultIgnored = 0,
        };

        var toml = TomlSerializer.Serialize(empty, context.IgnoreConditionModel);
        Assert.DoesNotContain("optional", toml);
        Assert.DoesNotContain("alwaysIgnored", toml);
        Assert.DoesNotContain("defaultIgnored", toml);

        var populated = new IgnoreConditionModel
        {
            Optional = "x",
            AlwaysIgnored = "should_not_appear",
            DefaultIgnored = 1,
        };

        var toml2 = TomlSerializer.Serialize(populated, context.IgnoreConditionModel);
        var model = TomlSerializer.Deserialize<TomlTable>(toml2);

        Assert.NotNull(model);
        var nonNullModel = model!;
        Assert.Contains("optional", nonNullModel);
        Assert.Contains("defaultIgnored", nonNullModel);
        Assert.DoesNotContain("alwaysIgnored", nonNullModel);
    }

    [Fact]
    public void Reflection_RespectsIgnoreConditions()
    {
        var populated = new IgnoreConditionModel
        {
            Optional = "x",
            AlwaysIgnored = "should_not_appear",
            DefaultIgnored = 1,
        };

        var toml = TomlSerializer.Serialize(populated, options: TomlSerializerOptions.Default with { PropertyNamingPolicy = null });

        Assert.Contains("Optional", toml);
        Assert.Contains("DefaultIgnored", toml);
        Assert.DoesNotContain("AlwaysIgnored", toml);
    }

    [Fact]
    public void Deserialize_SkipsIgnoredMembers()
    {
        var context = TestTomlIgnoreContext.Default;
        var toml = """
            optional = "x"
            alwaysIgnored = "y"
            defaultIgnored = 2
            """;

        var model = TomlSerializer.Deserialize(toml, context.IgnoreConditionModel);

        Assert.NotNull(model);
        Assert.Equal("x", model!.Optional);
        Assert.Equal(2, model.DefaultIgnored);
        Assert.Equal("ignored", model.AlwaysIgnored);
    }

    [Fact]
    public void GeneratedContext_RespectsDirectionalIgnoreConditions()
    {
        var context = TestTomlIgnoreContext.Default;
        var toml = TomlSerializer.Serialize(new DirectionalIgnoreConditionModel
        {
            Keep = 1,
            Never = 2,
            TomlWriteOnly = 3,
            TomlReadOnly = 4,
        }, context.DirectionalIgnoreConditionModel);

        Assert.Contains("keep = 1", toml);
        Assert.Contains("never = 2", toml);
        Assert.Contains("tomlReadOnly = 4", toml);
        Assert.DoesNotContain("tomlWriteOnly", toml);

        var deserialized = TomlSerializer.Deserialize("""
            keep = 10
            never = 20
            tomlWriteOnly = 30
            tomlReadOnly = 40
            """, context.DirectionalIgnoreConditionModel);

        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized!.Keep);
        Assert.Equal(20, deserialized.Never);
        Assert.Equal(30, deserialized.TomlWriteOnly);
        Assert.Equal(0, deserialized.TomlReadOnly);
    }

    [Fact]
    public void Reflection_RespectsDirectionalIgnoreConditions()
    {
        var toml = TomlSerializer.Serialize(new DirectionalIgnoreConditionModel
        {
            Keep = 1,
            Never = 2,
            TomlWriteOnly = 3,
            TomlReadOnly = 4,
        });

        Assert.Contains("Keep = 1", toml);
        Assert.Contains("Never = 2", toml);
        Assert.Contains("TomlReadOnly = 4", toml);
        Assert.DoesNotContain("TomlWriteOnly", toml);

        var deserialized = TomlSerializer.Deserialize<DirectionalIgnoreConditionModel>("""
            Keep = 10
            Never = 20
            TomlWriteOnly = 30
            TomlReadOnly = 40
            """);

        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized!.Keep);
        Assert.Equal(20, deserialized.Never);
        Assert.Equal(30, deserialized.TomlWriteOnly);
        Assert.Equal(0, deserialized.TomlReadOnly);
    }

    [Fact]
    public void IgnoreConditionNever_OverridesDefaultIgnoreCondition()
    {
        var options = new TomlSerializerOptions { DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingDefault };

        var reflection = TomlSerializer.Serialize(new NeverIgnoreConditionModel(), options);
        var generated = TomlSerializer.Serialize(new NeverIgnoreConditionModel(), TestTomlNeverIgnoreContext.Default.NeverIgnoreConditionModel);

        Assert.Equal("TomlNever = 0\n", reflection);
        Assert.Equal(reflection, generated);
    }
}
