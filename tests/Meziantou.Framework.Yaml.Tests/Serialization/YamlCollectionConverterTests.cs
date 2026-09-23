using System.Collections.Concurrent;
using System.Collections.Frozen;
using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlCollectionConverterTests
{
    private sealed class EnumerableModel
    {
        public IEnumerable<int> Values { get; set; } = Array.Empty<int>();
    }

    [Fact]
    public void Array_RoundTrip()
    {
        var data = new[] { 1, 2, 3 };
        var yaml = YamlSerializer.Serialize(data);
        var roundTrip = YamlSerializer.Deserialize<int[]>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal(data, roundTrip);
    }

    [Fact]
    public void IEnumerable_RoundTripThroughObjectProperty()
    {
        var model = new EnumerableModel { Values = new[] { 1, 2, 3 } };
        var yaml = YamlSerializer.Serialize(model);

        var deserialized = YamlSerializer.Deserialize<EnumerableModel>(yaml);
        Assert.NotNull(deserialized);

        var values = deserialized.Values.ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, values);
    }

    [Fact]
    public void DictionaryKeyPolicy_AppliesToSerializedKeys()
    {
        var options = new YamlSerializerOptions { DictionaryKeyPolicy = YamlNamingPolicy.CamelCase };
        var yaml = YamlSerializer.Serialize(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["MyKey"] = 1,
                ["OtherKey"] = 2,
            },
            options);

        Assert.Contains("myKey: 1", yaml);
        Assert.Contains("otherKey: 2", yaml);
        Assert.DoesNotContain("MyKey:", yaml);
        Assert.DoesNotContain("OtherKey:", yaml);
    }

    [Fact]
    public void DictionaryKeyPolicy_PascalCase_AppliesToSerializedKeys()
    {
        var options = new YamlSerializerOptions { DictionaryKeyPolicy = YamlNamingPolicy.PascalCase };
        var yaml = YamlSerializer.Serialize(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["myKey"] = 1,
                ["OtherKey"] = 2,
            },
            options);

        Assert.Contains("MyKey: 1", yaml);
        Assert.Contains("OtherKey: 2", yaml);
        Assert.DoesNotContain("myKey:", yaml);
    }

    [Fact]
    public void Indentation_RespectsIndentSize()
    {
        var yaml = YamlSerializer.Serialize(
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal)
            {
                ["outer"] = new Dictionary<string, int>(StringComparer.Ordinal) { ["inner"] = 1 },
            },
            new YamlSerializerOptions { IndentSize = 4 });

        Assert.Contains("outer:\n    inner: 1\n", yaml);
    }

    [Fact]
    public void MultiDimensionalArray_IsNotSupported()
    {
        var value = Array.CreateInstance(typeof(int), 1, 2);
        var exception = Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(value, value.GetType()));

        Assert.Contains("Multi-dimensional", exception.Message);
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Deserialize("- - 1\n", value.GetType()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedList_RoundTrip(bool useSourceGeneration)
    {
        var model = new CollectionConverterModel { Linked = new LinkedList<int>([1, 2]) };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConverterModel>(yaml, useSourceGeneration);
        var root = Deserialize<LinkedList<int>>("- 3\n- 4\n", useSourceGeneration);

        Assert.Contains("Linked:\n  - 1\n  - 2\n", yaml);
        Assert.Equal(new[] { 1, 2 }, roundTrip!.Linked!);
        Assert.Equal(new[] { 3, 4 }, root!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutableDictionaryTypes_RoundTripAsMappings(bool useSourceGeneration)
    {
        var model = new CollectionConverterModel
        {
            Sorted = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["b"] = 2, ["a"] = 1 },
            Concurrent = new ConcurrentDictionary<string, int>(StringComparer.Ordinal) { ["c"] = 3 },
            SortedByNumber = new SortedDictionary<int, string> { [2] = "two", [1] = "one" },
            Derived = new DerivedStringDictionary { ["d"] = 4 },
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConverterModel>(yaml, useSourceGeneration)!;
        var root = Deserialize<SortedDictionary<string, int>>("z: 26\n<<: {y: 25}\n", useSourceGeneration)!;

        Assert.Contains("Sorted:\n  a: 1\n  b: 2\n", yaml);
        Assert.Contains("Concurrent:\n  c: 3\n", yaml);
        Assert.Contains("SortedByNumber:\n  1: one\n  2: two\n", yaml);
        Assert.Contains("Derived:\n  d: 4\n", yaml);
        Assert.Equal(model.Sorted, roundTrip.Sorted!);
        Assert.Equal(3, roundTrip.Concurrent!["c"]);
        Assert.Equal(model.SortedByNumber, roundTrip.SortedByNumber!);
        Assert.Equal(4, roundTrip.Derived!["d"]);
        Assert.Equal(new Dictionary<string, int>(StringComparer.Ordinal) { ["y"] = 25, ["z"] = 26 }, root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringKeyedDictionaryInterfaces_ReadLikeDictionary(bool useSourceGeneration)
    {
        const string Yaml = "null: 1\n<<: {merged: 2}\n";

        var dictionary = Deserialize<IDictionary<string, int>>(Yaml, useSourceGeneration)!;
        var readOnlyDictionary = Deserialize<IReadOnlyDictionary<string, int>>(Yaml, useSourceGeneration)!;
        var caseInsensitive = Deserialize<IDictionary<string, int>>("Key: 1\n", useSourceGeneration, static options => options with { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(1, dictionary["null"]);
        Assert.Equal(2, dictionary["merged"]);
        Assert.Equal(1, readOnlyDictionary["null"]);
        Assert.Equal(2, readOnlyDictionary["merged"]);
        Assert.Equal(1, caseInsensitive["key"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergeSequence_EarlierMappingsTakePrecedence(bool useSourceGeneration)
    {
        const string Yaml = "<<: [{a: 1}, {a: 3, b: 4}]\n";

        var root = Deserialize<Dictionary<string, int>>(Yaml, useSourceGeneration)!;
        var member = Deserialize<CollectionConverterModel>("Sorted:\n  <<: [{a: 1}, {a: 3, b: 4}]\n", useSourceGeneration)!;

        Assert.Equal(1, root["a"]);
        Assert.Equal(4, root["b"]);
        Assert.Equal(1, member.Sorted!["a"]);
        Assert.Equal(4, member.Sorted["b"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateKeys_AreDetectedWhenTheSchemaDisablesMergeKeys(bool useSourceGeneration)
    {
        static YamlSerializerOptions Json(YamlSerializerOptions options) => options with { Schema = YamlSchemaKind.Json };

        Assert.Throws<YamlException>(() => Deserialize<Dictionary<string, int>>("a: 1\na: 2\n", useSourceGeneration, Json));
        Assert.Throws<YamlException>(() => Deserialize<CollectionConverterModel>("Sorted:\n  a: 1\n  a: 2\n", useSourceGeneration, Json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateOnlyAndTimeOnlyKeys_UseRoundTripFormat(bool useSourceGeneration)
    {
        var model = new CollectionConverterModel
        {
            DateKeys = new Dictionary<DateOnly, int> { [new DateOnly(2024, 1, 2)] = 1 },
            TimeKeys = new Dictionary<TimeOnly, int> { [new TimeOnly(3, 4, 5, 6)] = 2 },
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConverterModel>(yaml, useSourceGeneration)!;

        Assert.Contains("DateKeys:\n  2024-01-02: 1\n", yaml);
        Assert.Contains("TimeKeys:\n  03:04:05.0060000: 2\n", yaml);
        Assert.Equal(model.DateKeys, roundTrip.DateKeys!);
        Assert.Equal(model.TimeKeys, roundTrip.TimeKeys!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullKeyOfReferenceType_Throws(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<Dictionary<Uri, int>>("~: 1\n", useSourceGeneration));

        Assert.Contains("Dictionary key cannot be null.", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HalfElements_ReadSpecialValues(bool useSourceGeneration)
    {
        var values = Deserialize<List<Half>>("- .inf\n- -.inf\n- .nan\n- 1_0\n", useSourceGeneration)!;
        var model = Deserialize<CollectionConverterModel>("Ratio: .inf\n", useSourceGeneration)!;

        Assert.Equal(Half.PositiveInfinity, values[0]);
        Assert.Equal(Half.NegativeInfinity, values[1]);
        Assert.True(Half.IsNaN(values[2]));
        Assert.Equal((Half)10, values[3]);
        Assert.Equal(Half.PositiveInfinity, model.Ratio);
        Assert.Equal("- .inf\n- -.inf\n", Serialize(new List<Half> { Half.PositiveInfinity, Half.NegativeInfinity }, useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElementsAndKeys_HonorUseSchema(bool useSourceGeneration)
    {
        static YamlSerializerOptions Json(YamlSerializerOptions options) => options with { UseSchema = true, Schema = YamlSchemaKind.Json };
        static YamlSerializerOptions Core(YamlSerializerOptions options) => options with { UseSchema = true, Schema = YamlSchemaKind.Core };

        Assert.Throws<YamlException>(() => Deserialize<List<int>>("- '1'\n", useSourceGeneration, Core));
        Assert.Throws<YamlException>(() => Deserialize<List<int>>("- 0x2A\n", useSourceGeneration, Json));
        Assert.Throws<YamlException>(() => Deserialize<List<int>>("- !!float 1\n", useSourceGeneration, Json));
        Assert.Throws<YamlException>(() => Deserialize<Dictionary<int, int>>("'1': 1\n", useSourceGeneration, Core));
        Assert.Equal(new[] { 42 }, Deserialize<List<int>>("- 0x2A\n", useSourceGeneration, Core)!);
        Assert.Equal(new[] { 42d }, Deserialize<List<double>>("- 0x2A\n", useSourceGeneration, Core)!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void YamlNodeMember_NullAssignsNull(bool useSourceGeneration)
    {
        var model = Deserialize<CollectionConverterModel>("Mapping: null\nSequence: ~\n", useSourceGeneration)!;

        Assert.Null(model.Mapping);
        Assert.Null(model.Sequence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void YamlSequenceRoot_KeepsTheNodeModel(bool useSourceGeneration)
    {
        var sequence = Deserialize<YamlSequence>("&items !!seq\n- 'a'\n", useSourceGeneration)!;

        Assert.Equal("items", sequence.Anchor);
        Assert.Equal("tag:yaml.org,2002:seq", sequence.Tag);
        Assert.Throws<YamlException>(() => Deserialize<YamlSequence>("~\n", useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void YamlNode_RoundTripKeepsScalarStylesAndTags(bool useSourceGeneration)
    {
        const string Yaml = "'null': '1'\nplain: null\ntagged: !!str 2\n";

        var node = Deserialize<YamlNode>(Yaml, useSourceGeneration);
        var yaml = Serialize(node, useSourceGeneration);
        var values = Deserialize<Dictionary<string, object>>(yaml, useSourceGeneration)!;

        Assert.Equal(Yaml, yaml, ignoreLineEndingDifferences: true);
        Assert.Equal("1", values["null"]);
        Assert.Null(values["plain"]);
        Assert.Equal("2", values["tagged"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedCollections_RoundTripAsSequences(bool useSourceGeneration)
    {
        var model = new CollectionConverterModel
        {
            Queue = new Queue<int>([1, 2]),
            Stack = new Stack<string>(["bottom", "top"]),
            ConcurrentQueue = new ConcurrentQueue<int>([3, 4]),
            ConcurrentStack = new ConcurrentStack<int>([5, 6]),
            ConcurrentBag = new ConcurrentBag<int>([7]),
            Segment = new ArraySegment<int>([0, 8, 9, 0], 1, 2),
            FrozenSet = new[] { "a" }.ToFrozenSet(StringComparer.Ordinal),
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConverterModel>(yaml, useSourceGeneration)!;

        Assert.Contains("Queue:\n  - 1\n  - 2\n", yaml);
        Assert.Contains("Stack:\n  - top\n  - bottom\n", yaml);
        Assert.Contains("ConcurrentQueue:\n  - 3\n  - 4\n", yaml);
        Assert.Contains("ConcurrentStack:\n  - 6\n  - 5\n", yaml);
        Assert.Contains("ConcurrentBag:\n  - 7\n", yaml);
        Assert.Contains("Segment:\n  - 8\n  - 9\n", yaml);
        Assert.Contains("FrozenSet:\n  - a\n", yaml);
        Assert.Equal(new[] { 1, 2 }, roundTrip.Queue!);
        Assert.Equal(new[] { "top", "bottom" }, roundTrip.Stack!);
        Assert.Equal(new[] { 3, 4 }, roundTrip.ConcurrentQueue!);
        Assert.Equal(new[] { 6, 5 }, roundTrip.ConcurrentStack!);
        Assert.Equal(new[] { 7 }, roundTrip.ConcurrentBag!);
        Assert.Equal(new[] { 8, 9 }, roundTrip.Segment);
        Assert.Equal(new[] { "a" }, roundTrip.FrozenSet!);
        Assert.Null(Deserialize<CollectionConverterModel>("Queue: null\n", useSourceGeneration)!.Queue);
        Assert.Equal(yaml, Serialize(roundTrip, useSourceGeneration));

        Assert.Equal("- c\n- b\n- a\n", Serialize(new Stack<string>(["a", "b", "c"]), useSourceGeneration));
        Assert.Equal(new[] { "c", "b", "a" }, Deserialize<Stack<string>>("- c\n- b\n- a\n", useSourceGeneration)!);
        Assert.Equal("null\n", Serialize(default(ArraySegment<int>), useSourceGeneration));
        Assert.Null(Deserialize<ArraySegment<int>>("null\n", useSourceGeneration).Array);
        Assert.Equal(new[] { 1, 2 }, Deserialize<Queue<int>>("[1, 2]\n", useSourceGeneration)!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrozenDictionary_RoundTripsAsMapping(bool useSourceGeneration)
    {
        var model = new CollectionConverterModel
        {
            FrozenDictionary = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 }.ToFrozenDictionary(StringComparer.Ordinal),
            FrozenNumberDictionary = new Dictionary<int, string> { [2] = "two" }.ToFrozenDictionary(),
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConverterModel>(yaml, useSourceGeneration)!;
        var root = Deserialize<FrozenDictionary<string, int>>("Key: 1\n<<: {other: 2}\n", useSourceGeneration, static options => options with { PropertyNameCaseInsensitive = true })!;

        Assert.Contains("FrozenDictionary:\n  a: 1\nFrozenNumberDictionary:\n  2: two\n", yaml);
        Assert.Equal(1, roundTrip.FrozenDictionary!["a"]);
        Assert.Equal("two", roundTrip.FrozenNumberDictionary![2]);
        Assert.Equal(1, root["key"]);
        Assert.Equal(2, root["other"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyValuePair_RoundTripsAsKeyAndValueMapping(bool useSourceGeneration)
    {
        var model = new CollectionConverterModel
        {
            Pair = new KeyValuePair<string, int>("a", 1),
            Pairs = [new KeyValuePair<int, string?>(2, "b"), new KeyValuePair<int, string?>(3, null)],
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConverterModel>(yaml, useSourceGeneration)!;
        var camelCase = Serialize(new KeyValuePair<string, int>("c", 4), useSourceGeneration, static options => options with { PropertyNamingPolicy = YamlNamingPolicy.CamelCase });

        Assert.Contains("Pair:\n  Key: a\n  Value: 1\nPairs:\n  - Key: 2\n    Value: b\n  - Key: 3\n    Value: null\n", yaml);
        Assert.Equal(model.Pair, roundTrip.Pair);
        Assert.Equal(model.Pairs, roundTrip.Pairs!);
        Assert.Equal("key: c\nvalue: 4\n", camelCase);
        Assert.Equal(new KeyValuePair<string, int>("d", 5), Deserialize<KeyValuePair<string, int>>("value: 5\nextra: [1]\nKEY: d\n", useSourceGeneration, static options => options with { PropertyNameCaseInsensitive = true }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectKeyedDictionary_KeepsTheTypeOfItsKeys(bool useSourceGeneration)
    {
        var value = new Dictionary<object, object>
        {
            ["text"] = 1,
            ["2"] = "string key",
            [2] = "number key",
            ["null"] = "null text",
            [true] = "boolean key",
        };

        var yaml = Serialize(value, useSourceGeneration);
        var roundTrip = Deserialize<Dictionary<object, object>>(yaml, useSourceGeneration)!;

        Assert.Equal("text: 1\n\"2\": string key\n2: number key\n\"null\": null text\ntrue: boolean key\n", yaml);
        Assert.Equal("string key", roundTrip["2"]);
        // An untyped integer is read as a long, like an integer value of an object member.
        Assert.Equal("number key", roundTrip[2L]);
        Assert.Equal("null text", roundTrip["null"]);
        Assert.Equal("boolean key", roundTrip[true]);
        Assert.Throws<YamlException>(() => Deserialize<Dictionary<object, object>>("~: 1\n", useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullLikeDictionaryKeys_AreQuoted(bool useSourceGeneration)
    {
        var yaml = Serialize(new Dictionary<string, int>(StringComparer.Ordinal) { ["null"] = 1, ["~"] = 2, ["NULL"] = 3, ["1"] = 4 }, useSourceGeneration);
        var uriYaml = Serialize(new Dictionary<Uri, int> { [new Uri("null", UriKind.Relative)] = 1, [new Uri("~", UriKind.Relative)] = 2 }, useSourceGeneration);

        Assert.Equal("\"null\": 1\n\"~\": 2\n\"NULL\": 3\n1: 4\n", yaml);
        Assert.Equal("\"null\": 1\n\"~\": 2\n", uriYaml);
        Assert.Equal(2, Deserialize<Dictionary<Uri, int>>(uriYaml, useSourceGeneration)![new Uri("~", UriKind.Relative)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectRoot_RoundTrips(bool useSourceGeneration)
    {
        object value = new Dictionary<string, object> { ["a"] = 1 };

        var yaml = useSourceGeneration
            ? YamlSerializer.Serialize(value, CollectionConverterYamlContext.Default.Object)
            : YamlSerializer.Serialize(value, typeof(object));
        var roundTrip = useSourceGeneration
            ? YamlSerializer.Deserialize(yaml, CollectionConverterYamlContext.Default.Object)
            : YamlSerializer.Deserialize<object>(yaml);

        Assert.Equal("a: 1\n", yaml);
        Assert.Equal(1, Assert.IsType<Dictionary<object, object?>>(roundTrip)["a"]);
        Assert.Equal(yaml, Serialize<object>(value, useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionConstructorParameters_AreRead(bool useSourceGeneration)
    {
        var value = new CollectionConstructorRecord(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 },
            [2, 3],
            [4],
            ["x"],
            new Queue<int>([5]));

        var yaml = Serialize(value, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConstructorRecord>(yaml, useSourceGeneration)!;

        Assert.Equal("Map:\n  a: 1\nItems:\n  - 2\n  - 3\nArray:\n  - 4\nNames:\n  - x\nQueue:\n  - 5\n", yaml);
        Assert.Equal(1, roundTrip.Map["a"]);
        Assert.Equal(new[] { 2, 3 }, roundTrip.Items);
        Assert.Equal(new[] { 4 }, roundTrip.Array);
        Assert.Equal(new[] { "x" }, roundTrip.Names);
        Assert.Equal(new[] { 5 }, roundTrip.Queue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionInterfaceElements_RoundTrip(bool useSourceGeneration)
    {
        var model = new CollectionConverterModel
        {
            Nested = [[1, 2], [3]],
            NestedMap = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal) { ["a"] = [4] },
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<CollectionConverterModel>(yaml, useSourceGeneration)!;
        var root = Deserialize<List<IEnumerable<int>>>("- [1]\n- []\n", useSourceGeneration)!;

        Assert.Contains("Nested:\n  -\n    - 1\n    - 2\n  -\n    - 3\nNestedMap:\n  a:\n    - 4\n", yaml);
        Assert.Equal(new[] { 1, 2 }, roundTrip.Nested![0]);
        Assert.Equal(new[] { 3 }, roundTrip.Nested[1]);
        Assert.Equal(new[] { 4 }, roundTrip.NestedMap!["a"]);
        Assert.Equal(new[] { 1 }, root[0]);
        Assert.Empty(root[1]);
    }

    [Fact]
    public void UnsupportedTypes_ThrowNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Deserialize<System.Collections.ArrayList>("- 1\n"));
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(new System.Collections.Hashtable { ["a"] = 1 }));
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(typeof(int)));
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize<Func<int>>(() => 1));
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(new UnsupportedCollectionModel()));
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Deserialize<IAsyncEnumerable<int>>("- 1\n"));
        Assert.Contains("is enumerable", Assert.ThrowsAny<Exception>(() => YamlSerializer.Serialize(new EnumerableWithoutMembers())).Message);
        Assert.Contains("is enumerable", Assert.ThrowsAny<Exception>(() => YamlSerializer.Deserialize<EnumerableWithoutMembers>("{}\n")).Message);

        var keyException = Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(new Dictionary<CollectionConverterModel, int> { [new CollectionConverterModel()] = 1 }));
        Assert.Contains("Dictionary key type", keyException.Message);
        Assert.Throws<NotSupportedException>(() => YamlSerializer.Deserialize<Dictionary<CollectionConverterModel, int>>("a: 1\n"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableScalarElements_RoundTrip(bool useSourceGeneration)
    {
        var guid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var date = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var model = new NullableElementCollectionModel
        {
            Ints = [1, null],
            IntArray = [1, null],
            IntMap = new Dictionary<string, int?>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = null },
            Guids = [guid, null],
            Kinds = [NullableElementKind.Second, null],
            Dates = [date, null],
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<NullableElementCollectionModel>(yaml, useSourceGeneration)!;

        Assert.Equal(
            "Ints:\n  - 1\n  - null\n" +
            "IntArray:\n  - 1\n  - null\n" +
            "IntMap:\n  a: 1\n  b: null\n" +
            "Guids:\n  - 11111111-1111-1111-1111-111111111111\n  - null\n" +
            "Kinds:\n  - Second\n  - null\n" +
            "Dates:\n  - 2024-01-02T03:04:05.0000000Z\n  - null\n",
            yaml);
        Assert.Equal(model.Ints, roundTrip.Ints!);
        Assert.Equal(model.IntArray, roundTrip.IntArray!);
        Assert.Equal(model.IntMap, roundTrip.IntMap!);
        Assert.Equal(model.Guids, roundTrip.Guids!);
        Assert.Equal(model.Kinds, roundTrip.Kinds!);
        Assert.Equal(model.Dates, roundTrip.Dates!);
        Assert.Equal("- 1\n- null\n", Serialize(new List<int?> { 1, null }, useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonSchema_WritesEveryScalarThatIsNotANullBooleanOrNumberQuoted(bool useSourceGeneration)
    {
        static YamlSerializerOptions Json(YamlSerializerOptions options) => options with { UseSchema = true, Schema = YamlSchemaKind.Json };
        var model = CreateScalarFormattingModel();

        var yaml = Serialize(model, useSourceGeneration, Json);
        var roundTrip = Deserialize<ScalarFormattingModel>(yaml, useSourceGeneration, Json)!;

        Assert.Equal(
            "\"Name\": \"text\"\n" +
            "\"Count\": 1\n" +
            "\"Double\": 1.0\n" +
            "\"Single\": 2.0\n" +
            "\"Half\": 3.0\n" +
            "\"Decimal\": 4\n" +
            "\"Infinity\": !!float .inf\n" +
            "\"Flag\": true\n" +
            "\"Nothing\": null\n" +
            "\"Kind\": \"Second\"\n" +
            "\"When\": \"2024-01-02\"\n" +
            "\"Id\": \"00000000-0000-0000-0000-000000000001\"\n" +
            "\"DoubleKeys\":\n" +
            "  1.0: \"one\"\n",
            yaml);
        Assert.Equal(model.Name, roundTrip.Name);
        Assert.Equal(model.Kind, roundTrip.Kind);
        Assert.Equal(model.When, roundTrip.When);
        Assert.Equal(model.Id, roundTrip.Id);
        Assert.Equal(double.PositiveInfinity, roundTrip.Infinity);
        Assert.Equal("one", roundTrip.DoubleKeys![1.0]);
    }

    [Theory]
    [InlineData(false, "Name: text\n")]
    [InlineData(false, "\"Name\": text\n")]
    [InlineData(false, "Name: \"text\"\n")]
    [InlineData(false, "\"Nothing\": ~\n")]
    [InlineData(false, "\"Flag\": True\n")]
    [InlineData(false, "\"Nothing\":\n")]
    [InlineData(false, "\"Count\": 0x1\n")]
    [InlineData(true, "Name: text\n")]
    [InlineData(true, "\"Name\": text\n")]
    [InlineData(true, "\"Nothing\": ~\n")]
    [InlineData(true, "\"Flag\": True\n")]
    public void JsonSchema_RejectsPlainScalarsThatAreNotANullBooleanOrNumber(bool useSourceGeneration, string yaml)
    {
        static YamlSerializerOptions Json(YamlSerializerOptions options) => options with { UseSchema = true, Schema = YamlSchemaKind.Json };

        Assert.Throws<YamlException>(() => Deserialize<ScalarFormattingModel>(yaml, useSourceGeneration, Json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonSchema_AcceptsQuotedStringsAndTaggedPlainScalars(bool useSourceGeneration)
    {
        static YamlSerializerOptions Json(YamlSerializerOptions options) => options with { UseSchema = true, Schema = YamlSchemaKind.Json };

        var model = Deserialize<ScalarFormattingModel>("\"Name\": !!str text\n\"Count\": 2\n\"Flag\": true\n\"Nothing\": null\n\"Kind\": 'Second'\n", useSourceGeneration, Json)!;

        Assert.Equal("text", model.Name);
        Assert.Equal(2, model.Count);
        Assert.True(model.Flag);
        Assert.Null(model.Nothing);
        Assert.Equal(NullableElementKind.Second, model.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeFloatingPointNumbers_AreWrittenAsFloats(bool useSourceGeneration)
    {
        var yaml = Serialize(CreateScalarFormattingModel(), useSourceGeneration);

        Assert.Contains("Double: 1.0\nSingle: 2.0\nHalf: 3.0\nDecimal: 4\n", yaml);
        Assert.Contains("DoubleKeys:\n  1.0: one\n", yaml);
        Assert.Equal("- 10000000000000000.0\n- 1E+300\n- -0.0\n- 0.5\n", Serialize(new List<double> { 1e16, 1e300, -0.0, 0.5 }, useSourceGeneration));

        var untyped = Assert.IsType<Dictionary<object, object?>>(YamlSerializer.Deserialize<object>(yaml));
        Assert.Equal(1.0, untyped["Double"]);
        Assert.Equal(2.0, untyped["Single"]);
        Assert.Equal(3.0, untyped["Half"]);
        Assert.Equal("one", Assert.IsType<Dictionary<object, object?>>(untyped["DoubleKeys"])[1.0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreferPlainStyleDisabled_QuotesStrings(bool useSourceGeneration)
    {
        static YamlSerializerOptions Quoted(YamlSerializerOptions options) => options with { ScalarStylePreferences = new YamlScalarStylePreferences { PreferPlainStyle = false } };
        var model = new ScalarFormattingModel { Name = "text", Count = 1, Flag = true, Kind = NullableElementKind.Second };

        var yaml = Serialize(model, useSourceGeneration, Quoted);

        Assert.StartsWith("\"Name\": \"text\"\n\"Count\": 1\n\"Double\": 0.0\n", yaml);
        Assert.Contains("\"Flag\": true\n\"Nothing\": null\n\"Kind\": Second\n", yaml);
        Assert.Equal("text", Deserialize<ScalarFormattingModel>(yaml, useSourceGeneration)!.Name);
    }

    [Fact]
    public void PreferPlainStyleDisabled_IsHonoredFromTheSourceGenerationOptions()
    {
        var yaml = YamlSerializer.Serialize(new ScalarFormattingModel { Name = "text" }, QuotedStringsYamlContext.Default);

        Assert.StartsWith("\"Name\": \"text\"\n", yaml);
    }

    [Fact]
    public void PreferPlainStyleDisabled_DoesNotOverrideAnExplicitStringStyle()
    {
        var options = new YamlSerializerOptions { ScalarStylePreferences = new YamlScalarStylePreferences { PreferPlainStyle = false, StringStyle = ScalarStyle.Plain } };

        Assert.Equal("text\n", YamlSerializer.Serialize("text", options));
    }

    private static ScalarFormattingModel CreateScalarFormattingModel() => new()
    {
        Name = "text",
        Count = 1,
        Double = 1.0,
        Single = 2f,
        Half = (Half)3,
        Decimal = 4m,
        Infinity = double.PositiveInfinity,
        Flag = true,
        Kind = NullableElementKind.Second,
        When = new DateOnly(2024, 1, 2),
        Id = new Guid("00000000-0000-0000-0000-000000000001"),
        DoubleKeys = new Dictionary<double, string> { [1.0] = "one" },
    };

    private static string Serialize<T>(T value, bool useSourceGeneration, Func<YamlSerializerOptions, YamlSerializerOptions>? configure = null)
    {
        configure ??= static options => options;
        return useSourceGeneration
            ? YamlSerializer.Serialize(value, CollectionConverterYamlContext.Default.CreateOptions(configure))
            : YamlSerializer.Serialize(value, configure(new YamlSerializerOptions()));
    }

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration, Func<YamlSerializerOptions, YamlSerializerOptions>? configure = null)
    {
        configure ??= static options => options;
        return useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, CollectionConverterYamlContext.Default.CreateOptions(configure))
            : YamlSerializer.Deserialize<T>(yaml, configure(new YamlSerializerOptions()));
    }
}

#pragma warning disable MA0048 // File name must match type name
internal sealed class DerivedStringDictionary : Dictionary<string, int>
{
}

internal sealed class CollectionConverterModel
{
    public LinkedList<int>? Linked { get; set; }

    public SortedDictionary<string, int>? Sorted { get; set; }

    public ConcurrentDictionary<string, int>? Concurrent { get; set; }

    public SortedDictionary<int, string>? SortedByNumber { get; set; }

    public DerivedStringDictionary? Derived { get; set; }

    public Dictionary<DateOnly, int>? DateKeys { get; set; }

    public Dictionary<TimeOnly, int>? TimeKeys { get; set; }

    public Half Ratio { get; set; }

    public YamlMapping? Mapping { get; set; }

    public YamlSequence? Sequence { get; set; }

    public Queue<int>? Queue { get; set; }

    public Stack<string>? Stack { get; set; }

    public ConcurrentQueue<int>? ConcurrentQueue { get; set; }

    public ConcurrentStack<int>? ConcurrentStack { get; set; }

    public ConcurrentBag<int>? ConcurrentBag { get; set; }

    public ArraySegment<int> Segment { get; set; }

    public FrozenSet<string>? FrozenSet { get; set; }

    public FrozenDictionary<string, int>? FrozenDictionary { get; set; }

    public FrozenDictionary<int, string>? FrozenNumberDictionary { get; set; }

    public KeyValuePair<string, int> Pair { get; set; }

    public List<KeyValuePair<int, string?>>? Pairs { get; set; }

    public List<IEnumerable<int>>? Nested { get; set; }

    public Dictionary<string, IReadOnlyList<int>>? NestedMap { get; set; }
}

internal enum NullableElementKind
{
    First,
    Second,
}

internal sealed class NullableElementCollectionModel
{
    public List<int?>? Ints { get; set; }

    public int?[]? IntArray { get; set; }

    public Dictionary<string, int?>? IntMap { get; set; }

    public List<Guid?>? Guids { get; set; }

    public List<NullableElementKind?>? Kinds { get; set; }

    public List<DateTime?>? Dates { get; set; }
}

internal sealed class ScalarFormattingModel
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public double Double { get; set; }

    public float Single { get; set; }

    public Half Half { get; set; }

    public decimal Decimal { get; set; }

    public double Infinity { get; set; }

    public bool Flag { get; set; }

    public string? Nothing { get; set; }

    public NullableElementKind Kind { get; set; }

    public DateOnly When { get; set; }

    public Guid Id { get; set; }

    public Dictionary<double, string>? DoubleKeys { get; set; }
}

internal sealed record CollectionConstructorRecord(Dictionary<string, int> Map, List<int> Items, int[] Array, IReadOnlyList<string> Names, Queue<int> Queue);

internal readonly struct EnumerableWithoutMembers : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator() => Enumerable.Range(1, 2).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class UnsupportedCollectionModel
{
    public System.Collections.ArrayList? Items { get; set; } = [];
}

[YamlSerializable(typeof(CollectionConverterModel))]
[YamlSerializable(typeof(LinkedList<int>))]
[YamlSerializable(typeof(SortedDictionary<string, int>))]
[YamlSerializable(typeof(IDictionary<string, int>))]
[YamlSerializable(typeof(IReadOnlyDictionary<string, int>))]
[YamlSerializable(typeof(Dictionary<Uri, int>))]
[YamlSerializable(typeof(Dictionary<int, int>))]
[YamlSerializable(typeof(Dictionary<string, int>))]
[YamlSerializable(typeof(List<Half>))]
[YamlSerializable(typeof(List<int>))]
[YamlSerializable(typeof(List<double>))]
[YamlSerializable(typeof(YamlSequence))]
[YamlSerializable(typeof(YamlNode))]
[YamlSerializable(typeof(Dictionary<string, object>))]
[YamlSerializable(typeof(Dictionary<object, object>))]
[YamlSerializable(typeof(object))]
[YamlSerializable(typeof(Stack<string>))]
[YamlSerializable(typeof(Queue<int>))]
[YamlSerializable(typeof(ArraySegment<int>))]
[YamlSerializable(typeof(FrozenDictionary<string, int>))]
[YamlSerializable(typeof(KeyValuePair<string, int>))]
[YamlSerializable(typeof(CollectionConstructorRecord))]
[YamlSerializable(typeof(List<IEnumerable<int>>))]
[YamlSerializable(typeof(NullableElementCollectionModel))]
[YamlSerializable(typeof(List<int?>))]
[YamlSerializable(typeof(ScalarFormattingModel))]
internal sealed partial class CollectionConverterYamlContext : YamlSerializerContext
{
}

[YamlSourceGenerationOptions(PreferPlainStyle = false)]
[YamlSerializable(typeof(ScalarFormattingModel))]
internal sealed partial class QuotedStringsYamlContext : YamlSerializerContext
{
}
