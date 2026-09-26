using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tomlyn.Tests;

public sealed class NewApiExtensionDataTests
{
    private sealed class ExtensionDataModel
    {
        public int Known { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object?> Extra { get; set; } = new();
    }

    private sealed class NullableExtensionDataModel
    {
        public int Known { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object?>? Extra { get; set; }
    }

    [Fact]
    public void Deserialize_StoresUnknownKeys_IntoExtensionData()
    {
        var model = TomlSerializer.Deserialize<ExtensionDataModel>("Known = 1\nUnknown = \"x\"\n");
        Assert.NotNull(model);
        Assert.Equal(1, model!.Known);
        Assert.Contains("Unknown", model.Extra);
        Assert.Equal("x", model.Extra["Unknown"]);
    }

    [Fact]
    public void Serialize_MergesExtensionData_IntoSurroundingTable()
    {
        var model = new ExtensionDataModel
        {
            Known = 1,
            Extra = new Dictionary<string, object?> { ["Unknown"] = "x" },
        };

        var toml = TomlSerializer.Serialize(model);
        Assert.Contains("Known", toml);
        Assert.Contains("Unknown", toml);
    }

    [Fact]
    public void Serialize_ThrowsOnExtensionDataKeyConflict()
    {
        var model = new ExtensionDataModel
        {
            Known = 1,
            Extra = new Dictionary<string, object?> { ["Known"] = 2 },
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(model));
    }

    [Fact]
    public void Deserialize_InitializesNullExtensionDataDictionary_WhenSettable()
    {
        var model = TomlSerializer.Deserialize<NullableExtensionDataModel>("Known = 1\nUnknown = 2\n");
        Assert.NotNull(model);
        Assert.NotNull(model!.Extra);
        Assert.Contains("Unknown", model.Extra);
        Assert.Equal(2L, model.Extra["Unknown"]);
    }
}

