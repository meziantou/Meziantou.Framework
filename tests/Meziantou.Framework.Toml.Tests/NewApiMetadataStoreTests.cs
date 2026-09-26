using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiMetadataStoreTests
{
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
