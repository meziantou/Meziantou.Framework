using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Syntax;

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

public sealed class MetadataContainerCommentsModel
{
    public IList<long> Values { get; set; } = [];

    public MetadataNestedModel? Nested { get; set; }

    public string B { get; set; } = "";
}

public sealed record MetadataRecordModel(long A, string B);

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(MetadataCircle), "circle")]
public abstract class MetadataShape
{
    public string? Name { get; set; }
}

public sealed class MetadataCircle : MetadataShape
{
    public long Radius { get; set; }
}

public sealed class MetadataShapes
{
    public IList<MetadataShape> Items { get; set; } = [];
}

[TomlSerializable(typeof(MetadataFormattedModel))]
[TomlSerializable(typeof(MetadataRecordModel))]
[TomlSerializable(typeof(MetadataContainerCommentsModel))]
[TomlSerializable(typeof(MetadataShapes))]
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
    public void MetadataStore_TrailingCommentsOfArraysAndInlineTables_StayOnTheirLine()
    {
        const string Toml = "Values = [1, 2] # values\nNested = {C = 2} # nested\nB = 'x' # b\n";

        Assert.Equal(Toml, Roundtrip<TomlTable>(resolver: null));
        Assert.Equal(Toml, Roundtrip<MetadataContainerCommentsModel>(resolver: null));
        Assert.Equal(Toml, Roundtrip<MetadataContainerCommentsModel>(TestTomlMetadataContext.Default));

        // A comment inside a multi-line array has nowhere to go, and does not move to the next key
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };
        var multiline = TomlSerializer.Deserialize<TomlTable>("Values = [\n  1, # one\n  2,\n] # values\nB = 'x'\n", options);
        Assert.Equal("Values = [1, 2] # values\nB = 'x'\n", TomlSerializer.Serialize(multiline, options).ReplaceLineEndings("\n"));

        static string Roundtrip<T>(ITomlTypeInfoResolver? resolver)
        {
            var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore(), TypeInfoResolver = resolver };
            return TomlSerializer.Serialize(TomlSerializer.Deserialize<T>(Toml, options), options).ReplaceLineEndings("\n");
        }
    }

    [Fact]
    public void MetadataStore_DiscriminatorComments_Roundtrip()
    {
        const string Toml = "[[Items]]\n# first\nkind = 'circle' # kind\nName = \"a\"\nRadius = 1\n";

        Assert.Equal(Toml, Roundtrip(resolver: null));
        Assert.Equal(Toml, Roundtrip(TestTomlMetadataContext.Default));

        static string Roundtrip(ITomlTypeInfoResolver? resolver)
        {
            var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore(), TypeInfoResolver = resolver };
            return TomlSerializer.Serialize(TomlSerializer.Deserialize<MetadataShapes>(Toml, options), options).ReplaceLineEndings("\n");
        }
    }

    [Fact]
    public void Serialize_DoesNotModifyTheStoreOrShareMemberMetadata()
    {
        var store = new TomlMetadataStore();
        var options = new TomlSerializerOptions { MetadataStore = store };
        var first = new StyledChild();
        var firstMetadata = new TomlPropertiesMetadata();
        var textMetadata = new TomlPropertyMetadata();
        firstMetadata.SetProperty(nameof(StyledChild.Text), textMetadata);
        store.SetProperties(first, firstMetadata);

        TomlSerializer.Serialize(first, options);

        // The formatting metadata of Path is shared by every instance: in the store, a comment added to it would apply to all
        Assert.False(firstMetadata.ContainsProperty(nameof(StyledChild.Path)));
        Assert.Null(textMetadata.StringStyle);
    }

    [Theory]
    [InlineData(TokenKind.Comment, "# note\nadmin = true")]
    [InlineData(TokenKind.Comment, "note")]
    [InlineData(TokenKind.Whitespaces, " \nadmin = true")]
    [InlineData(TokenKind.NewLine, "\nadmin = true\n")]
    public void Serialize_InvalidMetadataTrivia_Throws(TokenKind kind, string text)
    {
        var store = new TomlMetadataStore();
        var model = new TomlTable { ["name"] = "safe" };
        var metadata = new TomlPropertiesMetadata();
        metadata.SetProperty("name", new TomlPropertyMetadata { TrailingTrivia = [new TomlSyntaxTriviaMetadata(kind, text)] });
        store.SetProperties(model, metadata);

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(model, new TomlSerializerOptions { MetadataStore = store }));
    }

    [Fact]
    public void Serialize_ValidMetadataTrivia_IsWritten()
    {
        var store = new TomlMetadataStore();
        var model = new TomlTable { ["name"] = "safe" };
        var metadata = new TomlPropertiesMetadata();
        metadata.SetProperty("name", new TomlPropertyMetadata { TrailingTrivia = [new TomlSyntaxTriviaMetadata(TokenKind.Whitespaces, " \t"), new TomlSyntaxTriviaMetadata(TokenKind.Comment, "# note\t1")] });
        store.SetProperties(model, metadata);

        Assert.Equal("name = \"safe\" \t# note\t1\n", TomlSerializer.Serialize(model, new TomlSerializerOptions { MetadataStore = store }).ReplaceLineEndings("\n"));
    }

    // A comment runs to the end of its line, so the next key or header must not be written on it
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialize_CommentAfterTheEndOfLine_DoesNotCommentOutTheNextKey(bool nextIsTable)
    {
        var store = new TomlMetadataStore();
        var model = new TomlTable { ["a"] = 1L };
        model["b"] = nextIsTable ? new TomlTable { ["c"] = 2L } : 2L;
        var metadata = new TomlPropertiesMetadata();
        metadata.SetProperty("a", new TomlPropertyMetadata { TrailingTriviaAfterEndOfLine = [new TomlSyntaxTriviaMetadata(TokenKind.Comment, "# note"), new TomlSyntaxTriviaMetadata(TokenKind.Whitespaces, " ")] });
        store.SetProperties(model, metadata);

        var toml = TomlSerializer.Serialize(model, new TomlSerializerOptions { MetadataStore = store });

        Assert.Contains("# note", toml, StringComparison.Ordinal);
        Assert.Equal(TomlSerializer.Serialize(model), TomlSerializer.Serialize(TomlSerializer.Deserialize<TomlTable>(toml)!));
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
