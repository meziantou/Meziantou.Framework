using System.Text.Json.Serialization;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Tomlyn.Tests;

#pragma warning disable MA0048 // File name must match type name

public sealed class IgnoreConditionModel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public int JsonWriteOnly { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public int JsonReadOnly { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(IgnoreConditionModel))]
[TomlSerializable(typeof(DirectionalIgnoreConditionModel))]
internal sealed partial class TestTomlIgnoreContext : TomlSerializerContext
{
}

public class NewApiIgnoreConditionTests
{
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
            JsonWriteOnly = 5,
            JsonReadOnly = 6,
        }, context.DirectionalIgnoreConditionModel);

        Assert.Contains("keep = 1", toml);
        Assert.Contains("never = 2", toml);
        Assert.Contains("tomlReadOnly = 4", toml);
        Assert.Contains("jsonReadOnly = 6", toml);
        Assert.DoesNotContain("tomlWriteOnly", toml);
        Assert.DoesNotContain("jsonWriteOnly", toml);

        var deserialized = TomlSerializer.Deserialize("""
            keep = 10
            never = 20
            tomlWriteOnly = 30
            tomlReadOnly = 40
            jsonWriteOnly = 50
            jsonReadOnly = 60
            """, context.DirectionalIgnoreConditionModel);

        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized!.Keep);
        Assert.Equal(20, deserialized.Never);
        Assert.Equal(30, deserialized.TomlWriteOnly);
        Assert.Equal(0, deserialized.TomlReadOnly);
        Assert.Equal(50, deserialized.JsonWriteOnly);
        Assert.Equal(0, deserialized.JsonReadOnly);
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
            JsonWriteOnly = 5,
            JsonReadOnly = 6,
        });

        Assert.Contains("Keep = 1", toml);
        Assert.Contains("Never = 2", toml);
        Assert.Contains("TomlReadOnly = 4", toml);
        Assert.Contains("JsonReadOnly = 6", toml);
        Assert.DoesNotContain("TomlWriteOnly", toml);
        Assert.DoesNotContain("JsonWriteOnly", toml);

        var deserialized = TomlSerializer.Deserialize<DirectionalIgnoreConditionModel>("""
            Keep = 10
            Never = 20
            TomlWriteOnly = 30
            TomlReadOnly = 40
            JsonWriteOnly = 50
            JsonReadOnly = 60
            """);

        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized!.Keep);
        Assert.Equal(20, deserialized.Never);
        Assert.Equal(30, deserialized.TomlWriteOnly);
        Assert.Equal(0, deserialized.TomlReadOnly);
        Assert.Equal(50, deserialized.JsonWriteOnly);
        Assert.Equal(0, deserialized.JsonReadOnly);
    }
}
