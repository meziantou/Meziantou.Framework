using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlMergeKeyTests
{
    [Fact]
    public void Deserialize_Object_ShouldApplyMergeKey()
    {
        var yaml =
            "<<: { A: 1, B: 2 }\n" +
            "B: 3\n";

        var result = YamlSerializer.Deserialize<MergePayload>(yaml);

        Assert.NotNull(result);
        Assert.Equal(1, result.A);
        Assert.Equal(3, result.B);
    }

    [Fact]
    public void Deserialize_Dictionary_ShouldApplyMergeKey()
    {
        var yaml =
            "<<: { a: 1, b: 2 }\n" +
            "b: 5\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml);

        Assert.NotNull(result);
        Assert.Equal(1, result["a"]);
        Assert.Equal(5, result["b"]);
    }

    [Fact]
    public void Deserialize_Dictionary_ShouldApplyMergeSequenceInOrder()
    {
        var yaml =
            "<<:\n" +
            "  - { a: 1 }\n" +
            "  - { a: 2, b: 3 }\n" +
            "c: 4\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml);

        Assert.NotNull(result);
        // The merge extension gives mappings earlier in the sequence precedence over later ones.
        Assert.Equal(1, result["a"]);
        Assert.Equal(3, result["b"]);
        Assert.Equal(4, result["c"]);
    }

    [Fact]
    public void Deserialize_MergeKey_ShouldBeIgnoredForJsonSchema()
    {
        var yaml =
            "<<: { A: 1, B: 2 }\n" +
            "B: 3\n";

        var result = YamlSerializer.Deserialize<MergePayload>(yaml, new YamlSerializerOptions { Schema = YamlSchemaKind.Json });

        Assert.NotNull(result);
        Assert.Equal(0, result.A);
        Assert.Equal(3, result.B);
    }

    [Fact]
    public void Deserialize_Object_ShouldApplyMergeAlias()
    {
        var yaml =
            "Defaults: &d\n" +
            "  Timeout: 30\n" +
            "  Retries: 2\n" +
            "Prod:\n" +
            "  <<: *d\n" +
            "  Timeout: 60\n";

        var result = YamlSerializer.Deserialize<Config>(yaml, PreserveOptions);

        Assert.NotNull(result);
        Assert.NotNull(result.Defaults);
        Assert.Equal(30, result.Defaults.Timeout);
        Assert.Equal(2, result.Defaults.Retries);
        Assert.NotNull(result.Prod);
        Assert.Equal(60, result.Prod.Timeout);
        Assert.Equal(2, result.Prod.Retries);
        Assert.NotSame(result.Defaults, result.Prod);
    }

    [Fact]
    public void Deserialize_Object_ShouldApplyMergeAlias_WhenLocalKeyIsBeforeMergeKey()
    {
        var yaml =
            "Defaults: &d\n" +
            "  Timeout: 30\n" +
            "  Retries: 2\n" +
            "Prod:\n" +
            "  Timeout: 60\n" +
            "  <<: *d\n";

        var result = YamlSerializer.Deserialize<Config>(yaml, PreserveOptions);

        Assert.NotNull(result?.Prod);
        Assert.Equal(60, result.Prod.Timeout);
        Assert.Equal(2, result.Prod.Retries);
    }

    [Fact]
    public void Deserialize_Object_ShouldApplyMergeSequenceMixingAliasesAndMappings()
    {
        var yaml =
            "Defaults: &d\n" +
            "  Timeout: 30\n" +
            "  Retries: 2\n" +
            "  Name: from-alias\n" +
            "Prod:\n" +
            "  <<:\n" +
            "    - *d\n" +
            "    - { Retries: 5 }\n" +
            "  Timeout: 60\n";

        var result = YamlSerializer.Deserialize<Config>(yaml, PreserveOptions);

        Assert.NotNull(result?.Prod);
        Assert.Equal(60, result.Prod.Timeout);
        Assert.Equal(5, result.Prod.Retries);
        Assert.Equal("from-alias", result.Prod.Name);
    }

    [Fact]
    public void Deserialize_ObjectWithConstructor_ShouldApplyMergeAlias()
    {
        var yaml =
            "Defaults: &d\n" +
            "  Timeout: 30\n" +
            "  Retries: 2\n" +
            "Prod:\n" +
            "  <<: *d\n" +
            "  Timeout: 60\n";

        var result = YamlSerializer.Deserialize<RecordConfig>(yaml, PreserveOptions);

        Assert.NotNull(result?.Prod);
        Assert.Equal(60, result.Prod.Timeout);
        Assert.Equal(2, result.Prod.Retries);
    }

    [Fact]
    public void Deserialize_Object_ShouldApplyMergeAliasFromDictionaryAnchor()
    {
        var yaml =
            "Defaults: &d\n" +
            "  Timeout: 30\n" +
            "  Retries: 2\n" +
            "Prod:\n" +
            "  <<: *d\n" +
            "  Timeout: 60\n";

        var result = YamlSerializer.Deserialize<DictionaryDefaultsConfig>(yaml, PreserveOptions);

        Assert.NotNull(result?.Prod);
        Assert.Equal(60, result.Prod.Timeout);
        Assert.Equal(2, result.Prod.Retries);
    }

    [Fact]
    public void Deserialize_PopulatedObject_ShouldApplyMergeAlias()
    {
        var yaml =
            "Defaults: &d\n" +
            "  Timeout: 30\n" +
            "  Retries: 2\n" +
            "  Name: from-alias\n" +
            "Prod:\n" +
            "  <<: *d\n" +
            "  Timeout: 60\n";

        var result = YamlSerializer.Deserialize<PopulateConfig>(yaml, PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal(60, result.Prod.Timeout);
        Assert.Equal(2, result.Prod.Retries);
        Assert.Equal("from-alias", result.Prod.Name);
    }

    [Fact]
    public void Deserialize_Object_ShouldThrowForMergeAliasWhenReferenceHandlingIsNotPreserve()
    {
        var yaml =
            "Defaults: &d\n" +
            "  Timeout: 30\n" +
            "Prod:\n" +
            "  <<: *d\n";

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Config>(yaml));

        Assert.Contains("ReferenceHandling", exception.Message);
    }

    private static YamlSerializerOptions PreserveOptions => new() { ReferenceHandling = YamlReferenceHandling.Preserve };

    private sealed class MergePayload
    {
        public int A { get; set; }

        public int B { get; set; }
    }

    private sealed class Config
    {
        public Section? Defaults { get; set; }

        public Section? Prod { get; set; }
    }

    private sealed class Section
    {
        public int Timeout { get; set; }

        public int Retries { get; set; }

        public string? Name { get; set; }
    }

    private sealed class RecordConfig
    {
        public SectionRecord? Defaults { get; set; }

        public SectionRecord? Prod { get; set; }
    }

    private sealed record SectionRecord(int Timeout, int Retries);

    private sealed class PopulateConfig
    {
        public Section? Defaults { get; set; }

        [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
        public Section Prod { get; } = new() { Name = "initial" };
    }

    private sealed class DictionaryDefaultsConfig
    {
        public Dictionary<string, int>? Defaults { get; set; }

        public Section? Prod { get; set; }
    }

    [Fact]
    public void Deserialize_Object_ShouldApplyMergeSequenceInOrder()
    {
        var yaml =
            "<<:\n" +
            "  - { A: 1 }\n" +
            "  - { A: 2, B: 3 }\n";

        var result = YamlSerializer.Deserialize<MergePayload>(yaml);

        Assert.NotNull(result);
        Assert.Equal(1, result.A);
        Assert.Equal(3, result.B);
    }

    [Fact]
    public void Deserialize_UntypedObject_ShouldApplyMergeSequenceInOrder()
    {
        var yaml =
            "<<:\n" +
            "  - { a: 1 }\n" +
            "  - { a: 2, b: 3 }\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml);

        Assert.NotNull(result);
        Assert.Equal(1L, result["a"]);
        Assert.Equal(3L, result["b"]);
    }

    [Fact]
    public void Deserialize_Dictionary_ShouldApplyMergeAliasSequenceInOrder()
    {
        var yaml =
            "first: &f { a: 1 }\n" +
            "second: &s { a: 2, b: 3 }\n" +
            "merged:\n" +
            "  <<: [*f, *s]\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(yaml, PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal(1, result["merged"]["a"]);
        Assert.Equal(3, result["merged"]["b"]);
    }

    [Fact]
    public void Deserialize_Dictionary_ExplicitKeyWinsOverMergeWhateverItsPosition()
    {
        var before = YamlSerializer.Deserialize<Dictionary<string, int>>("a: 5\n<<: { a: 1 }\n");
        var after = YamlSerializer.Deserialize<Dictionary<string, int>>("<<: { a: 1 }\na: 5\n");

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(5, before["a"]);
        Assert.Equal(5, after["a"]);
    }

    [Theory]
    [InlineData("'<<': hello\n")]
    [InlineData("\"<<\": hello\n")]
    [InlineData("!!str << : hello\n")]
    public void Deserialize_Dictionary_QuotedOrTaggedMergeKeyIsAnOrdinaryKey(string yaml)
    {
        var result = YamlSerializer.Deserialize<Dictionary<string, string>>(yaml);

        Assert.NotNull(result);
        Assert.Equal("hello", result["<<"]);
    }

    [Theory]
    [InlineData("'<<': hello\n")]
    [InlineData("\"<<\": hello\n")]
    public void Deserialize_UntypedObject_QuotedMergeKeyIsAnOrdinaryKey(string yaml)
    {
        var result = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml);

        Assert.NotNull(result);
        Assert.Equal("hello", result["<<"]);
    }

    [Fact]
    public void Serialize_Dictionary_WithMergeKeyName_RoundTrips()
    {
        var value = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<<"] = "hello",
            ["other"] = "world",
        };

        var yaml = YamlSerializer.Serialize(value);
        var roundTrip = YamlSerializer.Deserialize<Dictionary<string, string>>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal("hello", roundTrip["<<"]);
        Assert.Equal("world", roundTrip["other"]);
    }
}

