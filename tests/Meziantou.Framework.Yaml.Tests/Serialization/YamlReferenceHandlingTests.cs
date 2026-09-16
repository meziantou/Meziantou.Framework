#pragma warning disable MA0048 // File name must match type name
using System.Collections;
using System.Collections.Immutable;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

internal sealed class ReferenceCallbackNode : IYamlOnSerializing, IYamlOnSerialized
{
    [YamlIgnore]
    public List<string>? Log { get; set; }

    public string Name { get; set; } = string.Empty;

    public ReferenceCallbackNode? First { get; set; }

    public ReferenceCallbackNode? Second { get; set; }

    [YamlIgnore]
    public bool ShareFirstWhenSerializing { get; set; }

    public void OnSerializing()
    {
        Log?.Add("ing:" + Name);
        if (ShareFirstWhenSerializing)
        {
            Second = First;
        }
    }

    public void OnSerialized() => Log?.Add("ed:" + Name);
}

internal struct ReferenceCallbackStruct : IYamlOnSerializing
{
    public int X { get; set; }

    public string? Log { get; set; }

    public void OnSerializing() => Log += "serializing;";
}

internal sealed class ReferenceCallbackStructHolder
{
    public ReferenceCallbackStruct Value { get; set; }
}

internal sealed class ReferenceCountingInt32Converter : YamlConverter<int>
{
    public List<bool> CollectingReferences { get; } = [];

    public override int Read(YamlReader reader)
    {
        var value = int.Parse(reader.GetScalarValue(), CultureInfo.InvariantCulture);
        reader.Read();
        return value;
    }

    public override void Write(YamlWriter writer, int value)
    {
        CollectingReferences.Add(writer.IsCollectingReferences);
        writer.WriteScalar(value);
    }
}

internal sealed class ReferenceCountedValue
{
    public int Value { get; set; }
}

internal sealed class ReferenceImmutableSets
{
    public ImmutableHashSet<int>? First { get; set; }

    public ImmutableHashSet<int>? Second { get; set; }
}

[YamlSerializable(typeof(ReferenceCallbackNode))]
[YamlSerializable(typeof(ReferenceCallbackStructHolder))]
[YamlSerializable(typeof(ReferenceCountedValue))]
[YamlSerializable(typeof(ReferenceImmutableSets))]
internal sealed partial class ReferenceHandlingYamlContext : YamlSerializerContext
{
    public ReferenceHandlingYamlContext()
    {
    }

    public ReferenceHandlingYamlContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

public class YamlReferenceHandlingTests
{
    private sealed class Node
    {
        public string Name { get; set; } = string.Empty;

        public Node? Next { get; set; }
    }

    private sealed class Container
    {
        public Node? A { get; set; }

        public Node? B { get; set; }
    }

    [Fact]
    public void SerializePreservesSelfReferenceForObjects()
    {
        var node = new Node { Name = "root" };
        node.Next = node;

        var yaml = YamlSerializer.Serialize(
            node,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        Assert.Contains("&id001", yaml);
        Assert.Contains("Next: *id001", yaml);
    }

    [Fact]
    public void DeserializePreservesSelfReferenceForObjects()
    {
        var yaml = "&id001\nName: root\nNext: *id001\n";

        var node = YamlSerializer.Deserialize<Node>(
            yaml,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        Assert.NotNull(node);
        Assert.Same(node, node.Next);
        Assert.Equal("root", node.Name);
    }

    [Fact]
    public void SerializePreservesSharedReferences()
    {
        var node = new Node { Name = "shared" };
        var container = new Container { A = node, B = node };

        var yaml = YamlSerializer.Serialize(
            container,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        var anchorStart = yaml.IndexOf("A: &", StringComparison.Ordinal);
        Assert.True(anchorStart >= 0, "Expected 'A' to be anchored.");
        anchorStart += "A: &".Length;

        var anchorEnd = yaml.IndexOf('\n', anchorStart, StringComparison.Ordinal);
        Assert.True(anchorEnd > anchorStart, "Expected an anchor name after 'A: &'.");

        var anchor = yaml.Substring(anchorStart, anchorEnd - anchorStart).Trim();
        Assert.NotEmpty(anchor);

        Assert.Contains($"B: *{anchor}", yaml);
    }

    [Fact]
    public void DeserializePreservesSharedReferences()
    {
        var yaml = "A: &id001\n  Name: shared\nB: *id001\n";

        var container = YamlSerializer.Deserialize<Container>(
            yaml,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        Assert.NotNull(container);
        Assert.NotNull(container.A);
        Assert.NotNull(container.B);
        Assert.Same(container.A, container.B);
        Assert.Equal("shared", container.A.Name);
    }

    [Fact]
    public void DeserializeAndSerializePreservesSelfReferenceForListsOfObject()
    {
        var yaml = "&id001\n- *id001\n";
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var list = YamlSerializer.Deserialize<List<object?>>(yaml, options);

        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Same(list, list[0]);

        var roundTrip = YamlSerializer.Serialize(list, options);
        Assert.Contains("&id001", roundTrip);
        Assert.Contains("*id001", roundTrip);
    }

    [Fact]
    public void DeserializeAndSerializePreservesSelfReferenceForDictionariesOfObject()
    {
        var yaml = "&id001\nself: *id001\n";
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var dict = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml, options);

        Assert.NotNull(dict);
        Assert.True(dict.TryGetValue("self", out var self));
        Assert.Same(dict, self);

        var roundTrip = YamlSerializer.Serialize(dict, options);
        Assert.Contains("&id001", roundTrip);
        Assert.Contains("self: *id001", roundTrip);
    }

    [Fact]
    public void PreserveMinimalOnlyAnchorsSharedReferences()
    {
        var node = new Node { Name = "shared" };
        var container = new Container { A = node, B = node };
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal };

        var yaml = YamlSerializer.Serialize(container, options);

        Assert.DoesNotStartWith("&", yaml);
        Assert.Contains("A: &id001", yaml);
        Assert.Contains("B: *id001", yaml);

        var roundTrip = YamlSerializer.Deserialize<Container>(yaml, options);
        Assert.NotNull(roundTrip);
        Assert.Same(roundTrip.A, roundTrip.B);
    }

    [Fact]
    public void PreserveMinimalPreservesCycles()
    {
        var node = new Node { Name = "root" };
        node.Next = node;
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal };

        var yaml = YamlSerializer.Serialize(node, options);

        Assert.Contains("&id001", yaml);
        Assert.Contains("Next: *id001", yaml);

        var roundTrip = YamlSerializer.Deserialize<Node>(yaml, options);
        Assert.NotNull(roundTrip);
        Assert.Same(roundTrip, roundTrip.Next);
    }

    [Fact]
    public void DeserializeResolvesStringScalarAliases()
    {
        var result = YamlSerializer.Deserialize<List<string>>("[&a hello, *a]", PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal(["hello", "hello"], result);
    }

    [Fact]
    public void DeserializeResolvesNumericScalarAliases()
    {
        var result = YamlSerializer.Deserialize<List<int>>("[&a 42, *a]", PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal([42, 42], result);
    }

    [Fact]
    public void DeserializeResolvesNullScalarAliases()
    {
        var result = YamlSerializer.Deserialize<List<string?>>("[&a null, *a]", PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal([null, null], result);
    }

    [Fact]
    public void DeserializeResolvesScalarAliasesIntoObject()
    {
        var result = YamlSerializer.Deserialize<List<object?>>("[&a hello, *a, &b 42, *b]", PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal(["hello", "hello", 42L, 42L], result);
    }

    [Fact]
    public void DeserializeResolvesScalarAliasesInMappingValues()
    {
        var yaml =
            "a: &v hello\n" +
            "b: *v\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, string>>(yaml, PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal("hello", result["a"]);
        Assert.Equal("hello", result["b"]);
    }

    [Fact]
    public void DeserializeKeepsTheScalarStyleOfAnAliasedScalar()
    {
        // The anchored scalar is quoted, so the alias must not be resolved as a boolean either.
        var result = YamlSerializer.Deserialize<List<object?>>("[&a \"true\", *a]", PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal(["true", "true"], result);
    }

    [Fact]
    public void DeserializeUsesTheLastDefinitionOfAReusedAnchorName()
    {
        var yaml =
            "- &a hello\n" +
            "- *a\n" +
            "- &a world\n" +
            "- *a\n";

        var result = YamlSerializer.Deserialize<List<string>>(yaml, PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal(["hello", "hello", "world", "world"], result);
    }

    [Fact]
    public void DeserializeUsesTheLastDefinitionWhenAnAnchorNameChangesFromScalarToMapping()
    {
        var yaml =
            "first: &a hello\n" +
            "second: *a\n" +
            "third: &a { Name: n }\n" +
            "fourth: *a\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml, PreserveOptions);

        Assert.NotNull(result);
        Assert.Equal("hello", result["first"]);
        Assert.Equal("hello", result["second"]);
        Assert.IsType<Dictionary<string, object?>>(result["third"]);
        Assert.Same(result["third"], result["fourth"]);
    }

    [Fact]
    public void DeserializeSupportsAnchorNamesBeyondAsciiLettersAndDigits()
    {
        foreach (var anchor in new[] { "a.b", "a/b", "a:b", "héllo", "a+b" })
        {
            var yaml = $"[&{anchor} hello, *{anchor}]";
            var result = YamlSerializer.Deserialize<List<string>>(yaml, PreserveOptions);

            Assert.NotNull(result);
            Assert.Equal(["hello", "hello"], result, anchor);
        }
    }

    [Fact]
    public void DeserializeRejectsAnchorNamesContainingAFlowIndicator()
    {
        _ = Assert.Throws<SyntaxErrorException>(() => YamlSerializer.Deserialize<List<string>>("[& hello]", PreserveOptions));
        _ = Assert.Throws<SyntaxErrorException>(() => YamlSerializer.Deserialize<List<string>>("[&, hello]", PreserveOptions));
    }

    [Theory]
    [InlineData(false, YamlReferenceHandling.None)]
    [InlineData(true, YamlReferenceHandling.None)]
    [InlineData(false, YamlReferenceHandling.PreserveMinimal)]
    [InlineData(true, YamlReferenceHandling.PreserveMinimal)]
    public void SerializationCallbacksAreInvokedOncePerObject(bool useSourceGeneration, YamlReferenceHandling referenceHandling)
    {
        var log = new List<string>();
        var child = new ReferenceCallbackNode { Name = "child", Log = log };
        var root = new ReferenceCallbackNode { Name = "root", Log = log, First = child };

        _ = Serialize(root, useSourceGeneration, new YamlSerializerOptions { ReferenceHandling = referenceHandling });

        Assert.Equal(["ing:root", "ing:child", "ed:child", "ed:root"], log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreserveMinimalAnchorsAReferenceSharedBySerializingCallback(bool useSourceGeneration)
    {
        var root = new ReferenceCallbackNode { Name = "root", First = new ReferenceCallbackNode { Name = "shared" }, ShareFirstWhenSerializing = true };

        var yaml = Serialize(root, useSourceGeneration, new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal });

        Assert.Equal("Name: root\nFirst: &id001\n  Name: shared\n  First: null\n  Second: null\nSecond: *id001\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreserveMinimalWritesTheMutationsOfAStructSerializingCallback(bool useSourceGeneration)
    {
        var holder = new ReferenceCallbackStructHolder { Value = new ReferenceCallbackStruct { X = 1 } };

        var yaml = Serialize(holder, useSourceGeneration, new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal });

        Assert.Equal("Value:\n  X: 1\n  Log: serializing;\n", yaml);
    }

    [Theory]
    [InlineData(false, YamlReferenceHandling.Preserve, new[] { false })]
    [InlineData(true, YamlReferenceHandling.Preserve, new[] { false })]
    [InlineData(false, YamlReferenceHandling.PreserveMinimal, new[] { true, false })]
    [InlineData(true, YamlReferenceHandling.PreserveMinimal, new[] { true, false })]
    public void ConvertersCanTellTheReferenceCollectionPassApart(bool useSourceGeneration, YamlReferenceHandling referenceHandling, bool[] expected)
    {
        var converter = new ReferenceCountingInt32Converter();
        var options = new YamlSerializerOptions { ReferenceHandling = referenceHandling, Converters = [converter] };

        _ = Serialize(new ReferenceCountedValue { Value = 1 }, useSourceGeneration, options);

        Assert.Equal(expected, converter.CollectingReferences);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedImmutableHashSetsAreAliased(bool useSourceGeneration)
    {
        var set = ImmutableHashSet.Create(1);
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal };

        var yaml = Serialize(new ReferenceImmutableSets { First = set, Second = set }, useSourceGeneration, options);
        var roundTripped = useSourceGeneration
            ? YamlSerializer.Deserialize<ReferenceImmutableSets>(yaml, new ReferenceHandlingYamlContext(options))
            : YamlSerializer.Deserialize<ReferenceImmutableSets>(yaml, options);

        Assert.Equal("First: &id001\n  - 1\nSecond: *id001\n", yaml);
        Assert.NotNull(roundTripped);
        Assert.Same(roundTripped.First, roundTripped.Second);
    }

    private struct BoxedSequence : IEnumerable<int>
    {
        public readonly IEnumerator<int> GetEnumerator()
        {
            yield return 1;
        }

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private struct BoxedDictionary : IReadOnlyDictionary<string, int>
    {
        private static readonly Dictionary<string, int> Entries = new(StringComparer.Ordinal) { ["a"] = 1 };

        public readonly int this[string key] => Entries[key];

        public readonly IEnumerable<string> Keys => Entries.Keys;

        public readonly IEnumerable<int> Values => Entries.Values;

        public readonly int Count => Entries.Count;

        public readonly bool ContainsKey(string key) => Entries.ContainsKey(key);

        public readonly bool TryGetValue(string key, out int value) => Entries.TryGetValue(key, out value);

        public readonly IEnumerator<KeyValuePair<string, int>> GetEnumerator() => Entries.GetEnumerator();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void BoxedStructCollectionsAreNotAnchored()
    {
        IEnumerable<int> sequence = new BoxedSequence();
        IReadOnlyDictionary<string, int> dictionary = new BoxedDictionary();
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal };

        Assert.Equal("- - 1\n- - 1\n", YamlSerializer.Serialize(new List<IEnumerable<int>> { sequence, sequence }, options with { BlockSequenceSequenceStyle = YamlSequenceItemStyle.Compact }));
        Assert.Equal("- a: 1\n- a: 1\n", YamlSerializer.Serialize(new List<IReadOnlyDictionary<string, int>> { dictionary, dictionary }, options));
    }

    private static string Serialize<T>(T value, bool useSourceGeneration, YamlSerializerOptions options)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, typeof(T), new ReferenceHandlingYamlContext(options))
            : YamlSerializer.Serialize(value, typeof(T), options);

    private static YamlSerializerOptions PreserveOptions { get; } = new() { ReferenceHandling = YamlReferenceHandling.Preserve };
}
