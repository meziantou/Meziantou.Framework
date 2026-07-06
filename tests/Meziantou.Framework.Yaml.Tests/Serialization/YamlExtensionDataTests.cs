using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlExtensionDataTests
{
    [Fact]
    public void Deserialize_CapturesUnknownKeysIntoDictionary()
    {
        var value = YamlSerializer.Deserialize<DictionaryExtensionDataModel>("A: 1\nB: 2\nC: test\n")!;

        Assert.Equal(1, value.A);
        Assert.Equal(2L, value.Extra["B"]);
        Assert.Equal("test", value.Extra["C"]);
    }

    [Fact]
    public void Serialize_EmitsExtensionDataDictionaryEntries()
    {
        var value = new DictionaryExtensionDataModel
        {
            A = 1,
            Extra = new Dictionary<string, object?>(StringComparer.Ordinal) { ["B"] = 2, ["C"] = "test" },
        };

        var yaml = YamlSerializer.Serialize(value);

        Assert.Contains("A: 1", yaml);
        Assert.Contains("B: 2", yaml);
        Assert.Contains("C: test", yaml);
    }

    [Fact]
    public void Deserialize_CreatesNullableExtensionDataDictionaryOnDemand()
    {
        var value = YamlSerializer.Deserialize<NullableDictionaryExtensionDataModel>("Known: 1\nExtraA: 2\nExtraB: null\n")!;

        Assert.Equal(1, value.Known);
        Assert.NotNull(value.Extra);
        Assert.Equal(2L, value.Extra["ExtraA"]);
        Assert.Null(value.Extra["ExtraB"]);
    }

    [Fact]
    public void Deserialize_LeavesNullableExtensionDataDictionaryNullWhenNoExtraKeys()
    {
        var value = YamlSerializer.Deserialize<NullableDictionaryExtensionDataModel>("Known: 1\n")!;

        Assert.Equal(1, value.Known);
        Assert.Null(value.Extra);
    }

    [Fact]
    public void Serialize_DoesNotEmitNullableExtensionDataDictionaryWhenNull()
    {
        var value = new NullableDictionaryExtensionDataModel { Known = 1, Extra = null };
        var yaml = YamlSerializer.Serialize(value);

        Assert.Contains("Known: 1", yaml);
        Assert.DoesNotContain("Extra:", yaml);
    }

    [Fact]
    public void Deserialize_JsonExtensionDataAttribute_IsRecognized()
    {
        var value = YamlSerializer.Deserialize<JsonDictionaryExtensionDataModel>("A: 1\nB: 2\n")!;

        Assert.Equal(1, value.A);
        Assert.Equal(2L, value.Extra["B"]);
    }

    [Fact]
    public void Deserialize_CapturesUnknownKeysIntoYamlMapping()
    {
        var value = YamlSerializer.Deserialize<MappingExtensionDataModel>("A: 1\nB: 2\n")!;

        Assert.Equal(1, value.A);

        YamlElement? captured = null;
        foreach (var pair in value.Extra)
        {
            if (pair.Key is YamlValue keyValue && keyValue.Value == "B")
            {
                captured = pair.Value;
                break;
            }
        }

        Assert.NotNull(captured);
        Assert.IsType(typeof(YamlValue), captured);
        Assert.Equal("2", ((YamlValue)captured!).Value);
    }

    [Fact]
    public void Serialize_EmitsExtensionDataMappingEntries()
    {
        var value = new MappingExtensionDataModel
        {
            A = 1,
            Extra = new YamlMapping(),
        };

        value.Extra.Add(new YamlValue("B"), new YamlValue(2));

        var yaml = YamlSerializer.Serialize(value);

        Assert.Contains("A: 1", yaml);
        Assert.Contains("B:", yaml);
        Assert.Contains("2", yaml);
    }

    private sealed class DictionaryExtensionDataModel
    {
        public int A { get; set; }

        [YamlExtensionData]
        public Dictionary<string, object?> Extra { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class NullableDictionaryExtensionDataModel
    {
        public int Known { get; set; }

        [YamlExtensionData]
        public Dictionary<string, object?>? Extra { get; set; }
    }

    private sealed class JsonDictionaryExtensionDataModel
    {
        public int A { get; set; }

        [YamlExtensionData]
        public Dictionary<string, object?> Extra { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class MappingExtensionDataModel
    {
        public int A { get; set; }

        [YamlExtensionData]
        public YamlMapping Extra { get; set; } = new();
    }
}
