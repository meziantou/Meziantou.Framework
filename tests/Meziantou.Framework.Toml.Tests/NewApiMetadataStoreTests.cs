using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name
public sealed class MetadataFormattedModel
{
    public long A { get; set; }

    public string B { get; set; } = "";

    public MetadataNestedModel? Nested { get; set; }
}

public sealed class MetadataNestedModel
{
    public long C { get; set; }
}

public sealed record MetadataRecordModel(long A, string B);

[TomlSerializable(typeof(MetadataFormattedModel))]
[TomlSerializable(typeof(MetadataRecordModel))]
internal sealed partial class TestTomlMetadataContext : TomlSerializerContext;

[TomlSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[TomlSerializable(typeof(MetadataFormattedModel))]
internal sealed partial class TestTomlMetadataCaseInsensitiveContext : TomlSerializerContext;
#pragma warning restore MA0048

public sealed class NewApiMetadataStoreTests
{
    [Fact]
    public void GeneratedContext_MetadataStore_RoundtripsLikeReflection()
    {
        const string Toml = "# comment\nA = 0x1F # hex\nB = 'lit'\n\n# nested\n[Nested]\nC = 0b101\n";
        const string RecordToml = "# comment\nA = 0o17\nB = 'lit' # end\n";

        var reflection = Roundtrip<MetadataFormattedModel>(Toml, resolver: null);
        var reflectionRecord = Roundtrip<MetadataRecordModel>(RecordToml, resolver: null);

        Assert.Equal("# comment\nA = 0x1f # hex\nB = 'lit'\n# nested\n[Nested]\nC = 0b101\n", reflection);
        Assert.Equal(reflection, Roundtrip<MetadataFormattedModel>(Toml, TestTomlMetadataContext.Default));
        Assert.Equal(reflection, Roundtrip<MetadataFormattedModel>(Toml, TestTomlMetadataCaseInsensitiveContext.Default));
        Assert.Equal(RecordToml, reflectionRecord);
        Assert.Equal(reflectionRecord, Roundtrip<MetadataRecordModel>(RecordToml, TestTomlMetadataContext.Default));

        static string Roundtrip<T>(string toml, ITomlTypeInfoResolver? resolver)
        {
            var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore(), TypeInfoResolver = resolver };
            return TomlSerializer.Serialize(TomlSerializer.Deserialize<T>(toml, options), options).ReplaceLineEndings("\n");
        }
    }

    [Fact]
    public void MetadataStore_InlineTableDisplayKind_IsKeptByTypedModels()
    {
        const string Toml = "A = 1\nB = 'x'\nNested = {C = 2}\n";

        Assert.Equal(Toml, Roundtrip(resolver: null));
        Assert.Equal(Toml, Roundtrip(TestTomlMetadataContext.Default));

        static string Roundtrip(ITomlTypeInfoResolver? resolver)
        {
            var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore(), TypeInfoResolver = resolver };
            return TomlSerializer.Serialize(TomlSerializer.Deserialize<MetadataFormattedModel>(Toml, options), options).ReplaceLineEndings("\n");
        }
    }

    [Fact]
    public void NoInlineDisplayKind_OverridesInlineTablePolicy()
    {
        var table = TomlSerializer.Deserialize<TomlTable>("Nested = {C = 2}\n")!;
        table.PropertiesMetadata ??= new TomlPropertiesMetadata();
        table.PropertiesMetadata.SetProperty("Nested", new TomlPropertyMetadata { DisplayKind = TomlPropertyDisplayKind.NoInline });

        var toml = TomlSerializer.Serialize(table, new TomlSerializerOptions { InlineTablePolicy = TomlInlineTablePolicy.Always });

        Assert.Equal("[Nested]\nC = 2\n", toml.ReplaceLineEndings("\n"));
    }

    private sealed class Sample
    {
    }

    [Fact]
    public void SetAndGetProperties_Works()
    {
        var store = new TomlMetadataStore();
        var instance = new Sample();
        var metadata = new TomlPropertiesMetadata();

        Assert.False(store.TryGetProperties(instance, out _));

        store.SetProperties(instance, metadata);
        Assert.True(store.TryGetProperties(instance, out var readBack));
        Assert.Same(metadata, readBack);

        store.SetProperties(instance, metadata: null);
        Assert.False(store.TryGetProperties(instance, out _));
    }

    [Fact]
    public void Deserialize_TomlTable_CapturesMetadata_WhenStoreEnabled()
    {
        var store = new TomlMetadataStore();
        var options = new TomlSerializerOptions { MetadataStore = store };

        var table = TomlSerializer.Deserialize<TomlTable>("a = 0x1 # comment\n", options);

        Assert.NotNull(table);
        Assert.True(store.TryGetProperties(table!, out var properties));
        Assert.NotNull(properties);
        Assert.True(properties!.TryGetProperty("a", out var propertyMetadata));
        Assert.NotNull(propertyMetadata);
        Assert.Equal(TomlPropertyDisplayKind.IntegerHexadecimal, propertyMetadata!.DisplayKind);
        Assert.NotNull(propertyMetadata.Span.FileName);
        Assert.Equal(0, propertyMetadata.Span.Start.Line);
        Assert.Equal(0, propertyMetadata.Span.Start.Column);
        Assert.NotNull(propertyMetadata.TrailingTrivia);
        Assert.NotEmpty(propertyMetadata.TrailingTrivia);
    }

    [Fact]
    public void Deserialize_TomlTable_CommentEndingWithASurrogatePair_IsCapturedWhole()
    {
        var store = new TomlMetadataStore();
        var options = new TomlSerializerOptions { MetadataStore = store };

        var table = TomlSerializer.Deserialize<TomlTable>("a = 1 # \U0001F600\n", options)!;

        Assert.True(store.TryGetProperties(table, out var properties));
        Assert.True(properties!.TryGetProperty("a", out var propertyMetadata));
        Assert.Contains(propertyMetadata!.TrailingTrivia!, trivia => trivia.Text == "# \U0001F600");
        Assert.Equal("a = 1 # \U0001F600\n", TomlSerializer.Serialize(table, options));
    }
}
