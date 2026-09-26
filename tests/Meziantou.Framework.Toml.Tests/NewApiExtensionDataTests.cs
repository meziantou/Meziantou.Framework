using System.Collections.Generic;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name
public sealed class TomlTableExtensionDataModel
{
    public int Known { get; set; }

    [Meziantou.Framework.Toml.Serialization.TomlExtensionData]
    public Model.TomlTable? Extra { get; set; }
}

public sealed class InitializedTomlTableExtensionDataModel
{
    public int Known { get; set; }

    [Meziantou.Framework.Toml.Serialization.TomlExtensionData]
    public Model.TomlTable Extra { get; set; } = new();
}

[Meziantou.Framework.Toml.Serialization.TomlSerializable(typeof(TomlTableExtensionDataModel))]
[Meziantou.Framework.Toml.Serialization.TomlSerializable(typeof(InitializedTomlTableExtensionDataModel))]
internal sealed partial class TomlTableExtensionDataContext : Meziantou.Framework.Toml.Serialization.TomlSerializerContext;
#pragma warning restore MA0048

public sealed class NewApiExtensionDataTests
{
    private const string TomlTableExtensionDataToml = "Known = 1\nUnknown = \"x\"\n\n[Sub]\ny = 2\n";

    [Fact]
    public void TomlTableExtensionData_RoundtripsInBothPaths()
    {
        AssertExtra(TomlSerializer.Deserialize<TomlTableExtensionDataModel>(TomlTableExtensionDataToml)?.Extra);
        AssertExtra(TomlSerializer.Deserialize(TomlTableExtensionDataToml, TomlTableExtensionDataContext.Default.TomlTableExtensionDataModel)?.Extra);
        AssertExtra(TomlSerializer.Deserialize<InitializedTomlTableExtensionDataModel>(TomlTableExtensionDataToml)?.Extra);
        AssertExtra(TomlSerializer.Deserialize(TomlTableExtensionDataToml, TomlTableExtensionDataContext.Default.InitializedTomlTableExtensionDataModel)?.Extra);

        var model = TomlSerializer.Deserialize<TomlTableExtensionDataModel>(TomlTableExtensionDataToml)!;
        const string Expected = "Known = 1\nUnknown = \"x\"\n[Sub]\ny = 2\n";
        Assert.Equal(Expected, TomlSerializer.Serialize(model));
        Assert.Equal(Expected, TomlSerializer.Serialize(model, TomlTableExtensionDataContext.Default.TomlTableExtensionDataModel));

        static void AssertExtra(Model.TomlTable? extra)
        {
            Assert.NotNull(extra);
            Assert.Equal("x", extra["Unknown"]);
            Assert.Equal(2L, ((Model.TomlTable)extra["Sub"])["y"]);
        }
    }

    [Fact]
    public void ExtensionData_WithParameterizedConstructor_CollectsUnmappedKeys()
    {
        const string Toml = "Name = \"api\"\nPort = 80\nTimeout = 30\n[Logging]\nLevel = \"debug\"\n";

        var value = TomlSerializer.Deserialize<ConstructorExtensionDataModel>(Toml)!;
        var disallowed = TomlSerializer.Deserialize<ConstructorExtensionDataModel>(Toml, new TomlSerializerOptions { UnmappedMemberHandling = TomlUnmappedMemberHandling.Disallow })!;

        Assert.Equal("api", value.Name);
        Assert.Equal(80, value.Port);
        Assert.NotNull(value.Extra);
        Assert.Equal(30L, value.Extra["Timeout"]);
        Assert.Equal("debug", ((Model.TomlTable)value.Extra["Logging"]!)["Level"]);
        Assert.HasCount(2, disallowed.Extra!);
    }

    private sealed record ConstructorExtensionDataModel(string Name, int Port)
    {
        [Serialization.TomlExtensionData]
        public Dictionary<string, object?>? Extra { get; set; }
    }

    private sealed class ExtensionDataModel
    {
        public int Known { get; set; }

        [Serialization.TomlExtensionData]
        public Dictionary<string, object?> Extra { get; set; } = new();
    }

    private sealed class NullableExtensionDataModel
    {
        public int Known { get; set; }

        [Serialization.TomlExtensionData]
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

