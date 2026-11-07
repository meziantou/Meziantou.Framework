#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlEnumAndNullableTests
{
    private enum Color
    {
        Red = 1,
        Green = 2,
    }

    [Fact]
    public void Enum_ReadsFromNameOrNumber()
    {
        Assert.Equal(Color.Green, YamlSerializer.Deserialize<Color>("green"));
        Assert.Equal(Color.Green, YamlSerializer.Deserialize<Color>("2"));
    }

    [Fact]
    public void Enum_InvalidValue_ThrowsYamlExceptionWithContext()
    {
        var options = new YamlSerializerOptions { SourceName = "colors.yaml" };
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Color>("unknown", options));

        Assert.Equal("colors.yaml", ex.SourceName);
        Assert.True(ex.Start.Index >= 0);
        Assert.Contains("unknown", ex.Message);
    }

    [Fact]
    public void Nullable_Primitives_RoundTrip()
    {
        int? value = 123;
        var yaml = YamlSerializer.Serialize(value);
        Assert.Equal(123, YamlSerializer.Deserialize<int?>(yaml));

        int? nullValue = null;
        var nullYaml = YamlSerializer.Serialize(nullValue);
        Assert.Null(YamlSerializer.Deserialize<int?>(nullYaml));
    }

    [Fact]
    public void Nullable_Elements_RoundTripInSequence()
    {
        var yaml = "- 1\n- null\n- 3\n";
        var values = YamlSerializer.Deserialize<int?[]>(yaml);

        Assert.NotNull(values);
        Assert.Equal(new int?[] { 1, null, 3 }, values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableStruct_RoundTrips(bool useSourceGeneration)
    {
        var value = new NullableStructHolder
        {
            Value = new NullableStructPoint { X = 1 },
            Items = [new NullableStructPoint { X = 2 }, null],
            Map = new Dictionary<string, NullableStructPoint?>(StringComparer.Ordinal) { ["a"] = null, ["b"] = new NullableStructPoint { X = 3 } },
        };

        var yaml = Serialize(value, useSourceGeneration);
        Assert.Equal("Value:\n  X: 1\nItems:\n  - X: 2\n  - null\nMap:\n  a: null\n  b:\n    X: 3\n", yaml);

        var result = Deserialize<NullableStructHolder>("Value: {X: 4}\nItems: [null, {X: 5}]\nMap: {c: null, d: {X: 6}}\n", useSourceGeneration)!;
        Assert.Equal(4, result.Value!.Value.X);
        Assert.Equal([null, new NullableStructPoint { X = 5 }], result.Items);
        Assert.Null(result.Map!["c"]);
        Assert.Equal(6, result.Map["d"]!.Value.X);

        Assert.Null(Deserialize<NullableStructHolder>("Value: null\n", useSourceGeneration)!.Value);
        Assert.Equal("null\n", Serialize<NullableStructPoint?>(null, useSourceGeneration));
        Assert.Equal("X: 7\n", Serialize<NullableStructPoint?>(new NullableStructPoint { X = 7 }, useSourceGeneration));
        Assert.Equal(8, Deserialize<NullableStructPoint?>("X: 8\n", useSourceGeneration)!.Value.X);
        Assert.Null(Deserialize<NullableStructPoint?>("null\n", useSourceGeneration));
        Assert.Equal(9, Deserialize<NullableStructConstructorModel>("Value: {X: 9}\n", useSourceGeneration)!.Value!.Value.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumMemberNamedNull_IsNotReadAsNull(bool useSourceGeneration)
    {
        var value = new NullNamedEnumHolder
        {
            Value = NullNamedEnum.Null,
            Optional = NullNamedEnum.Null,
            Items = [NullNamedEnum.Null, null, NullNamedEnum.True, NullNamedEnum.No],
            Keys = new Dictionary<NullNamedEnum, int> { [NullNamedEnum.Null] = 1 },
        };

        var yaml = Serialize(value, useSourceGeneration);
        var roundTrip = Deserialize<NullNamedEnumHolder>(yaml, useSourceGeneration)!;

        Assert.Equal("Value: \"Null\"\nOptional: \"Null\"\nItems:\n  - \"Null\"\n  - null\n  - True\n  - No\nKeys:\n  \"Null\": 1\n", yaml);
        Assert.Equal(NullNamedEnum.Null, roundTrip.Optional);
        Assert.Equal(value.Items, roundTrip.Items);
        Assert.Equal(1, roundTrip.Keys![NullNamedEnum.Null]);
        Assert.Equal(NullNamedEnum.Null, Deserialize<NullNamedEnum?>(Serialize<NullNamedEnum?>(NullNamedEnum.Null, useSourceGeneration), useSourceGeneration));
    }

#if NET11_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableScalarUnion_RoundTrips(bool useSourceGeneration)
    {
        Assert.Equal("Value: 5\n", Serialize(new NullableUnionHolder { Value = new NullableTestUnion(5) }, useSourceGeneration));
        Assert.Equal("Value: null\n", Serialize(new NullableUnionHolder(), useSourceGeneration));
        Assert.Equal("text\n", Serialize<NullableTestUnion?>(new NullableTestUnion("text"), useSourceGeneration));

        Assert.Equal("abc", Deserialize<NullableUnionHolder>("Value: abc\n", useSourceGeneration)!.Value!.Value.Value);
        Assert.Null(Deserialize<NullableUnionHolder>("Value: null\n", useSourceGeneration)!.Value);
        Assert.Equal(6, Deserialize<NullableTestUnion?>("6\n", useSourceGeneration)!.Value.Value);
        Assert.Null(Deserialize<NullableTestUnion?>("null\n", useSourceGeneration));
    }
#endif

    private static string Serialize<T>(T value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, NullableStructContext.Default)
            : YamlSerializer.Serialize(value);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, NullableStructContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);
}

internal enum NullNamedEnum
{
    Null,
    True,
    No,
}

internal sealed class NullNamedEnumHolder
{
    public NullNamedEnum Value { get; set; }

    public NullNamedEnum? Optional { get; set; }

    public List<NullNamedEnum?>? Items { get; set; }

    public Dictionary<NullNamedEnum, int>? Keys { get; set; }
}

internal struct NullableStructPoint
{
    public int X { get; set; }
}

internal sealed class NullableStructHolder
{
    public NullableStructPoint? Value { get; set; }

    public List<NullableStructPoint?>? Items { get; set; }

    public Dictionary<string, NullableStructPoint?>? Map { get; set; }
}

internal sealed class NullableStructConstructorModel
{
    public NullableStructConstructorModel(NullableStructPoint? value) => Value = value;

    public NullableStructPoint? Value { get; }
}

#if NET11_0_OR_GREATER
internal union NullableTestUnion(int, string);

internal sealed class NullableUnionHolder
{
    public NullableTestUnion? Value { get; set; }
}

[YamlSerializable(typeof(NullableUnionHolder))]
[YamlSerializable(typeof(NullableTestUnion?))]
#endif
[YamlSerializable(typeof(NullNamedEnumHolder))]
[YamlSerializable(typeof(NullNamedEnum?))]
[YamlSerializable(typeof(NullableStructHolder))]
[YamlSerializable(typeof(NullableStructPoint?))]
[YamlSerializable(typeof(NullableStructConstructorModel))]
internal sealed partial class NullableStructContext : YamlSerializerContext
{
}
