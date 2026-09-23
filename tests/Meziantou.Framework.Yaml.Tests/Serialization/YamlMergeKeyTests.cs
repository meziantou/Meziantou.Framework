#pragma warning disable MA0048 // File name must match type name
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
            "Prod:\n" +
            "  <<:\n" +
            "    - *d\n" +
            "    - { Retries: 5, Name: from-mapping }\n" +
            "  Timeout: 60\n";

        var result = YamlSerializer.Deserialize<Config>(yaml, PreserveOptions);

        // The alias comes first in the merge sequence, so its Retries wins over the one of the later mapping.
        Assert.NotNull(result?.Prod);
        Assert.Equal(60, result.Prod.Timeout);
        Assert.Equal(2, result.Prod.Retries);
        Assert.Equal("from-mapping", result.Prod.Name);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_MergeAlias_MergesTheEntriesOfTheAnchoredMappingNode(bool useSourceGeneration)
    {
        var yaml =
            "Defaults: &d { A: 1 }\n" +
            "Other: &e { A: 3, B: 4 }\n" +
            "Prod: { <<: [*d, *e] }\n" +
            "Record: { <<: *e, A: 9 }\n" +
            "InitOnly: { <<: [*d, *e] }\n" +
            "Map: { <<: [*d, *e] }\n" +
            "Untyped: { <<: *e, C: 5 }\n" +
            "Any: { <<: *d }\n";

        var result = DeserializeWithPreserve<MergeAliasRoot>(yaml, useSourceGeneration);

        // Defaults was deserialized with B = 0, but its mapping node has no B, so the later mapping provides it.
        Assert.NotNull(result);
        Assert.Equal(1, result.Prod!.A);
        Assert.Equal(4, result.Prod.B);
        Assert.Equal(9, result.Record!.A);
        Assert.Equal(4, result.Record.B);
        Assert.Equal(1, result.InitOnly!.A);
        Assert.Equal(4, result.InitOnly.B);
        Assert.Equal(new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 1, ["B"] = 4 }, result.Map!);
        Assert.Equal(3L, result.Untyped!["A"]);
        Assert.Equal(4L, result.Untyped["B"]);
        Assert.Equal(5L, result.Untyped["C"]);
        Assert.Equal(1L, Assert.IsType<Dictionary<object, object?>>(result.Any)["A"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_MergeAlias_ReplaysNestedMergeAliases(bool useSourceGeneration)
    {
        var yaml =
            "Defaults: &b { A: 1 }\n" +
            "Other: &m { <<: *b, B: 2 }\n" +
            "Prod: { <<: *m }\n" +
            "Map: { <<: *m }\n";

        var result = DeserializeWithPreserve<MergeAliasRoot>(yaml, useSourceGeneration);

        Assert.NotNull(result);
        Assert.Equal(1, result.Prod!.A);
        Assert.Equal(2, result.Prod.B);
        Assert.Equal(new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 1, ["B"] = 2 }, result.Map!);
    }

    [Theory]
    [InlineData(false, "Defaults: &d 1\nProd: { <<: *d }\n")]
    [InlineData(false, "Map: &d { A: 1 }\nUntyped: &s [1]\nProd: { <<: *s }\n")]
    [InlineData(false, "Prod: { <<: *unknown }\n")]
    [InlineData(false, "Prod: &s { <<: *s }\n")]
    [InlineData(false, "Map: &s { <<: [*s] }\n")]
    [InlineData(true, "Defaults: &d 1\nProd: { <<: *d }\n")]
    [InlineData(true, "Map: &d { A: 1 }\nUntyped: &s [1]\nProd: { <<: *s }\n")]
    [InlineData(true, "Prod: { <<: *unknown }\n")]
    [InlineData(true, "Prod: &s { <<: *s }\n")]
    [InlineData(true, "Map: &s { <<: [*s] }\n")]
    public void Deserialize_MergeAlias_ThatDoesNotReferToAnEarlierMappingThrows(bool useSourceGeneration, string yaml)
    {
        Assert.Throws<YamlException>(() => DeserializeWithPreserve<MergeAliasRoot>(yaml, useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_MergeAlias_IsBoundedByMaxAliasExpansionNodeCount(bool useSourceGeneration)
    {
        var yaml =
            "a: &a { x1: 1, x2: 2, x3: 3 }\n" +
            "b: &b { <<: [*a, *a, *a] }\n" +
            "c: { <<: [*b, *b, *b] }\n";

        var exception = Assert.Throws<YamlException>(() => DeserializeWithPreserve<Dictionary<string, Dictionary<string, int>>>(yaml, useSourceGeneration, maxAliasExpansionNodeCount: 30));
        Assert.Contains("alias expansion", exception.Message, StringComparison.Ordinal);

        var result = DeserializeWithPreserve<Dictionary<string, Dictionary<string, int>>>(yaml, useSourceGeneration, maxAliasExpansionNodeCount: 200);
        Assert.NotNull(result);
        Assert.Equal(3, result["c"]["x3"]);
    }

    private static T? DeserializeWithPreserve<T>(string yaml, bool useSourceGeneration, int maxAliasExpansionNodeCount = 0)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, MergeKeyYamlContext.Default.CreateOptions(o => o with { ReferenceHandling = YamlReferenceHandling.Preserve, MaxAliasExpansionNodeCount = maxAliasExpansionNodeCount }))
            : YamlSerializer.Deserialize<T>(yaml, PreserveOptions with { MaxAliasExpansionNodeCount = maxAliasExpansionNodeCount });

    private static YamlSerializerOptions PreserveOptions => new() { ReferenceHandling = YamlReferenceHandling.Preserve };

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, MergeKeyYamlContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);

    internal sealed class MergePayload
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

    internal sealed record MergeRecord(int B, int A = 0);

    internal sealed class MergeInitOnlyPayload
    {
        public int A { get; init; }

        public required int B { get; init; }
    }

    private sealed class PopulateConfig
    {
        public Section? Defaults { get; set; }

        [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
        public Section Prod { get; } = new() { Name = "initial" };
    }

    internal sealed class MergeAliasRoot
    {
        public MergePayload? Defaults { get; set; }

        public MergePayload? Other { get; set; }

        public MergePayload? Prod { get; set; }

        public MergeRecord? Record { get; set; }

        public MergeInitOnlyPayload? InitOnly { get; set; }

        public Dictionary<string, int>? Map { get; set; }

        public Dictionary<string, object?>? Untyped { get; set; }

        public object? Any { get; set; }
    }

    internal sealed class MergeDictionaryHolder
    {
        public Dictionary<string, string>? Values { get; set; }
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

    [Theory]
    [InlineData(false, "<<: [{ A: 1 }, { A: 3, B: 4 }]\n")]
    [InlineData(false, "<<: [{ <<: { A: 1 } }, { A: 3, B: 4 }]\n")]
    [InlineData(true, "<<: [{ A: 1 }, { A: 3, B: 4 }]\n")]
    [InlineData(true, "<<: [{ <<: { A: 1 } }, { A: 3, B: 4 }]\n")]
    public void Deserialize_Object_EarlierMergeSequenceMappingsTakePrecedence(bool useSourceGeneration, string yaml)
    {
        var payload = Deserialize<MergePayload>(yaml, useSourceGeneration);
        var record = Deserialize<MergeRecord>(yaml, useSourceGeneration);
        var initOnly = Deserialize<MergeInitOnlyPayload>(yaml, useSourceGeneration);

        Assert.NotNull(payload);
        Assert.Equal(1, payload.A);
        Assert.Equal(4, payload.B);
        Assert.NotNull(record);
        Assert.Equal(1, record.A);
        Assert.Equal(4, record.B);
        Assert.NotNull(initOnly);
        Assert.Equal(1, initOnly.A);
        Assert.Equal(4, initOnly.B);
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
    [InlineData(false, "'<<': hello\n")]
    [InlineData(false, "\"<<\": hello\n")]
    [InlineData(false, "!!str << : hello\n")]
    [InlineData(true, "'<<': hello\n")]
    [InlineData(true, "\"<<\": hello\n")]
    [InlineData(true, "!!str << : hello\n")]
    public void Deserialize_Dictionary_QuotedOrTaggedMergeKeyIsAnOrdinaryKey(bool useSourceGeneration, string yaml)
    {
        var result = Deserialize<Dictionary<string, string>>(yaml, useSourceGeneration);

        Assert.NotNull(result);
        Assert.Equal("hello", result["<<"]);
    }

    [Theory]
    [InlineData(false, "'<<': hello\n")]
    [InlineData(false, "\"<<\": hello\n")]
    [InlineData(true, "'<<': hello\n")]
    [InlineData(true, "\"<<\": hello\n")]
    public void Deserialize_UntypedObject_QuotedMergeKeyIsAnOrdinaryKey(bool useSourceGeneration, string yaml)
    {
        var result = Deserialize<Dictionary<string, object?>>(yaml, useSourceGeneration);

        Assert.NotNull(result);
        Assert.Equal("hello", result["<<"]);
    }

    [Theory]
    [InlineData(false, "'<<': { A: 1 }\nB: 2\n")]
    [InlineData(false, "\"<<\": { A: 1 }\nB: 2\n")]
    [InlineData(false, "!!str << : { A: 1 }\nB: 2\n")]
    [InlineData(false, "<<: { \"<<\": { A: 1 }, B: 2 }\n")]
    [InlineData(true, "'<<': { A: 1 }\nB: 2\n")]
    [InlineData(true, "\"<<\": { A: 1 }\nB: 2\n")]
    [InlineData(true, "!!str << : { A: 1 }\nB: 2\n")]
    [InlineData(true, "<<: { \"<<\": { A: 1 }, B: 2 }\n")]
    public void Deserialize_Object_QuotedOrTaggedMergeKeyIsAnOrdinaryKey(bool useSourceGeneration, string yaml)
    {
        var result = Deserialize<MergePayload>(yaml, useSourceGeneration);

        Assert.NotNull(result);
        Assert.Equal(0, result.A);
        Assert.Equal(2, result.B);
    }

    [Theory]
    [InlineData(false, "<<: { A: 1 }\nB: 2\n")]
    [InlineData(false, "!!merge << : { A: 1 }\nB: 2\n")]
    [InlineData(false, "<<: { <<: { A: 1 }, B: 2 }\n")]
    [InlineData(true, "<<: { A: 1 }\nB: 2\n")]
    [InlineData(true, "!!merge << : { A: 1 }\nB: 2\n")]
    [InlineData(true, "<<: { <<: { A: 1 }, B: 2 }\n")]
    public void Deserialize_Object_PlainOrMergeTaggedMergeKeyIsApplied(bool useSourceGeneration, string yaml)
    {
        var result = Deserialize<MergePayload>(yaml, useSourceGeneration);

        Assert.NotNull(result);
        Assert.Equal(1, result.A);
        Assert.Equal(2, result.B);
    }

    [Theory]
    [InlineData(false, "\"<<\": { A: 1 }\nB: 2\n", 0)]
    [InlineData(false, "<<: { '<<': { A: 1 }, B: 2 }\n", 0)]
    [InlineData(false, "<<: { A: 1 }\nB: 2\n", 1)]
    [InlineData(true, "\"<<\": { A: 1 }\nB: 2\n", 0)]
    [InlineData(true, "<<: { '<<': { A: 1 }, B: 2 }\n", 0)]
    [InlineData(true, "<<: { A: 1 }\nB: 2\n", 1)]
    public void Deserialize_ObjectWithConstructor_OnlyPlainMergeKeyIsApplied(bool useSourceGeneration, string yaml, int expectedA)
    {
        var result = Deserialize<MergeRecord>(yaml, useSourceGeneration);

        Assert.NotNull(result);
        Assert.Equal(expectedA, result.A);
        Assert.Equal(2, result.B);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_DictionaryMember_QuotedMergeKeyIsAnOrdinaryKey(bool useSourceGeneration)
    {
        var result = Deserialize<MergeDictionaryHolder>("Values:\n  \"<<\": hello\n  other: world\n", useSourceGeneration);

        Assert.NotNull(result?.Values);
        Assert.Equal("hello", result.Values["<<"]);
        Assert.Equal("world", result.Values["other"]);
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

[YamlSerializable(typeof(YamlMergeKeyTests.MergePayload))]
[YamlSerializable(typeof(YamlMergeKeyTests.MergeRecord))]
[YamlSerializable(typeof(YamlMergeKeyTests.MergeInitOnlyPayload))]
[YamlSerializable(typeof(YamlMergeKeyTests.MergeDictionaryHolder))]
[YamlSerializable(typeof(YamlMergeKeyTests.MergeAliasRoot))]
[YamlSerializable(typeof(Dictionary<string, Dictionary<string, int>>))]
[YamlSerializable(typeof(Dictionary<string, string>))]
[YamlSerializable(typeof(Dictionary<string, object?>))]
internal sealed partial class MergeKeyYamlContext : YamlSerializerContext
{
}
