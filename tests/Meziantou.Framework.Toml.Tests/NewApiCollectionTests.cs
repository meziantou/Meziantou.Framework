using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable CA1002 // Test models use List<T> on purpose
#pragma warning disable CA1819 // Test models use arrays on purpose
#pragma warning disable MA0048 // File name must match type name

public sealed class GeneratedCollectionHolder
{
    public List<long> Values { get; set; } = new();

    public long[] Array { get; set; } = System.Array.Empty<long>();

    public IEnumerable<long> EnumerableValues { get; set; } = new List<long>();

    public IReadOnlyList<long> ReadOnlyValues { get; set; } = new List<long>();

    public IDictionary<string, long> Map { get; set; } = new Dictionary<string, long>();
}

public sealed class ObservableCollectionHolder
{
    public ObservableCollection<string> Global { get; set; } = new();
}

public sealed class SingleOrArrayObservableCollectionHolder
{
    [TomlSingleOrArray]
    public ObservableCollection<string> Global { get; set; } = new() { "existing" };
}

public sealed class DottedKeyDictionaryHolder
{
    public Dictionary<string, long> Map { get; set; } = new();
}

public sealed class Issue117ArrayHolder
{
    public int[]? Items { get; set; }
}

public sealed class SingleOrArrayCollectionHolder
{
    public SingleOrArrayCollectionHolder()
    {
        RuntimeIdentifiers = new List<string>();
    }

    [TomlSingleOrArray]
    [TomlPropertyName("rid")]
    public List<string> RuntimeIdentifiers { get; }
}

public sealed class SingleOrArraySettableCollectionHolder
{
    [TomlSingleOrArray]
    [TomlPropertyName("rid")]
    public List<string> RuntimeIdentifiers { get; set; } = new() { "existing" };
}

public sealed class SingleOrArrayImmutableArrayHolder
{
    [TomlSingleOrArray]
    public System.Collections.Immutable.ImmutableArray<string> Rid { get; set; }
}

// The collection of a get-only or constructor-bound member cannot be populated: the generated code compiles without warnings
public sealed class SingleOrArrayConstructorArrayHolder
{
    public SingleOrArrayConstructorArrayHolder(string[] rid)
    {
        Rid = rid;
    }

    [TomlSingleOrArray]
    public string[] Rid { get; }
}

public sealed class SingleOrArrayNullGetOnlyHolder
{
    [TomlSingleOrArray]
    public List<int> Rid { get; } = null!;
}

public sealed record SingleOrArrayRecordHolder([property: TomlSingleOrArray] List<string> Rid);

public sealed class SingleOrArrayGetOnlyImmutableHolder
{
    [TomlSingleOrArray]
    public System.Collections.Immutable.ImmutableList<int> Rid { get; } = [];

    [TomlSingleOrArray]
    public System.Collections.Immutable.ImmutableArray<int> Other { get; }
}

public sealed class TableArrayCollectionHolder
{
    public TableArrayItem[] Foo { get; set; } = [];

    public List<TableArrayItem> Bars { get; set; } = new();

    public int[] Values { get; set; } = [];
}

public sealed class TableArrayStyleOverrideHolder
{
    [TomlTableArrayStyle(TomlTableArrayStyle.InlineArrayOfTables)]
    public TableArrayItem[] Foo { get; set; } = [];
}

public sealed class TableArrayItem
{
    public string Name { get; set; } = string.Empty;
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedCollectionHolder))]
[TomlSerializable(typeof(ObservableCollectionHolder))]
[TomlSerializable(typeof(SingleOrArrayObservableCollectionHolder))]
[TomlSerializable(typeof(DottedKeyDictionaryHolder))]
[TomlSerializable(typeof(SingleOrArrayCollectionHolder))]
[TomlSerializable(typeof(SingleOrArraySettableCollectionHolder))]
[TomlSerializable(typeof(TableArrayCollectionHolder))]
[TomlSerializable(typeof(TableArrayStyleOverrideHolder))]
[TomlSerializable(typeof(SingleOrArrayImmutableArrayHolder))]
[TomlSerializable(typeof(SingleOrArrayConstructorArrayHolder))]
[TomlSerializable(typeof(SingleOrArrayRecordHolder))]
[TomlSerializable(typeof(SingleOrArrayNullGetOnlyHolder))]
[TomlSerializable(typeof(SingleOrArrayGetOnlyImmutableHolder))]
internal sealed partial class TestTomlCollectionsContext : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(Issue117ArrayHolder))]
internal sealed partial class TestTomlSnakeCaseCollectionsContext : TomlSerializerContext
{
}

public class NewApiCollectionTests
{
    [Fact]
    public void Reflection_CanRoundtripCollections()
    {
        var value = new GeneratedCollectionHolder
        {
            Values = new List<long> { 1, 2, 3 },
            Array = new long[] { 4, 5 },
            EnumerableValues = new List<long> { 6, 7 },
            ReadOnlyValues = new List<long> { 8, 9 },
            Map = new Dictionary<string, long> { ["x"] = 1, ["y"] = 2 },
        };

        var toml = TomlSerializer.Serialize(value);
        var roundtrip = TomlSerializer.Deserialize<GeneratedCollectionHolder>(toml);

        Assert.NotNull(roundtrip);
        Assert.Equal(new[] { 1L, 2L, 3L }, roundtrip!.Values);
        Assert.Equal(new[] { 4L, 5L }, roundtrip.Array);
        Assert.Equal(new[] { 6L, 7L }, roundtrip.EnumerableValues);
        Assert.Equal(new[] { 8L, 9L }, roundtrip.ReadOnlyValues);
        Assert.EqualUnordered(new Dictionary<string, long> { ["x"] = 1, ["y"] = 2 }, roundtrip.Map);
    }

    [Fact]
    public void GeneratedContext_CanRoundtripCollections()
    {
        var context = TestTomlCollectionsContext.Default;
        var value = new GeneratedCollectionHolder
        {
            Values = new List<long> { 1, 2, 3 },
            Array = new long[] { 4, 5 },
            EnumerableValues = new List<long> { 6, 7 },
            ReadOnlyValues = new List<long> { 8, 9 },
            Map = new Dictionary<string, long> { ["x"] = 1, ["y"] = 2 },
        };

        var toml = TomlSerializer.Serialize(value, context.GeneratedCollectionHolder);
        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedCollectionHolder);

        Assert.NotNull(roundtrip);
        Assert.Equal(new[] { 1L, 2L, 3L }, roundtrip!.Values);
        Assert.Equal(new[] { 4L, 5L }, roundtrip.Array);
        Assert.Equal(new[] { 6L, 7L }, roundtrip.EnumerableValues);
        Assert.Equal(new[] { 8L, 9L }, roundtrip.ReadOnlyValues);
        Assert.EqualUnordered(new Dictionary<string, long> { ["x"] = 1, ["y"] = 2 }, roundtrip.Map);
    }

    [Fact]
    public void Reflection_CanRoundtripObservableCollection()
    {
        var value = new ObservableCollectionHolder
        {
            Global = new ObservableCollection<string> { "Hello, World!" },
        };

        var toml = TomlSerializer.Serialize(value);
        var roundtrip = TomlSerializer.Deserialize<ObservableCollectionHolder>(toml);

        Assert.NotNull(roundtrip);
        Assert.IsAssignableTo<ObservableCollection<string>>(roundtrip!.Global);
        Assert.Equal(new[] { "Hello, World!" }, roundtrip.Global);
    }

    [Fact]
    public void GeneratedContext_CanRoundtripObservableCollection()
    {
        var context = TestTomlCollectionsContext.Default;
        var value = new ObservableCollectionHolder
        {
            Global = new ObservableCollection<string> { "Hello, World!" },
        };

        var toml = TomlSerializer.Serialize(value, context.ObservableCollectionHolder);
        var roundtrip = TomlSerializer.Deserialize(toml, context.ObservableCollectionHolder);

        Assert.NotNull(roundtrip);
        Assert.IsAssignableTo<ObservableCollection<string>>(roundtrip!.Global);
        Assert.Equal(new[] { "Hello, World!" }, roundtrip.Global);
    }

    [Fact]
    public void Reflection_SerializesObjectCollectionsAsTableArrays()
    {
        var value = new TableArrayCollectionHolder
        {
            Foo = [new TableArrayItem { Name = "one" }, new TableArrayItem { Name = "two" }],
            Bars = new List<TableArrayItem> { new() { Name = "three" } },
            Values = [1, 2],
        };

        var toml = TomlSerializer.Serialize(value);

        Assert.Contains("[[Foo]]", toml);
        Assert.Contains("[[Bars]]", toml);
        Assert.Contains("Values = [1, 2]", toml);
        Assert.True(toml.IndexOf("Values = [1, 2]", System.StringComparison.Ordinal) < toml.IndexOf("[[Foo]]", System.StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedContext_SerializesObjectCollectionsAsTableArrays()
    {
        var context = TestTomlCollectionsContext.Default;
        var value = new TableArrayCollectionHolder
        {
            Foo = [new TableArrayItem { Name = "one" }, new TableArrayItem { Name = "two" }],
            Bars = new List<TableArrayItem> { new() { Name = "three" } },
            Values = [1, 2],
        };

        var toml = TomlSerializer.Serialize(value, context.TableArrayCollectionHolder);

        Assert.Contains("[[foo]]", toml);
        Assert.Contains("[[bars]]", toml);
        Assert.Contains("values = [1, 2]", toml);
        Assert.True(toml.IndexOf("values = [1, 2]", System.StringComparison.Ordinal) < toml.IndexOf("[[foo]]", System.StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyObjectCollection_RemainsArray()
    {
        var toml = TomlSerializer.Serialize(new TableArrayCollectionHolder { Foo = [], Bars = [] });

        Assert.Contains("Foo = []", toml);
        Assert.Contains("Bars = []", toml);
    }

    [Fact]
    public void PropertyTableArrayStyleOverride_UsesInlineArrayOfTables()
    {
        var value = new TableArrayStyleOverrideHolder
        {
            Foo = [new TableArrayItem { Name = "one" }],
        };

        var reflectionToml = TomlSerializer.Serialize(value);
        var generatedToml = TomlSerializer.Serialize(value, TestTomlCollectionsContext.Default.TableArrayStyleOverrideHolder);

        Assert.Contains("Foo = [", reflectionToml);
        Assert.DoesNotContain("[[Foo]]", reflectionToml);
        Assert.Contains("foo = [", generatedToml);
        Assert.DoesNotContain("[[foo]]", generatedToml);
    }

    [Fact]
    public void GlobalInlineArrayOfTablesStyle_AppliesToTypedObjectCollections()
    {
        var options = new TomlSerializerOptions
        {
            TableArrayStyle = TomlTableArrayStyle.InlineArrayOfTables,
        };
        var value = new TableArrayCollectionHolder
        {
            Foo = [new TableArrayItem { Name = "one" }],
        };

        var toml = TomlSerializer.Serialize(value, options);

        Assert.Contains("Foo = [", toml);
        Assert.DoesNotContain("[[Foo]]", toml);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(33)]
    public void UntypedModel_CanReadPrimitiveArraysBeyondInitialTypedBufferCapacity(int itemCount)
    {
        var model = TomlSerializer.Deserialize<TomlTable>(CreateItemsToml(itemCount));

        Assert.NotNull(model);
        Assert.True(model!.TryGetValue("items", out var rawItems));
        Assert.IsAssignableTo<TomlArray>(rawItems);

        var items = (TomlArray)rawItems!;
        Assert.HasCount(itemCount, items);
        Assert.Equal(GetExpectedLongItems(itemCount), items.Cast<long>());
    }

    [Theory]
    [InlineData(17)]
    [InlineData(33)]
    public void Reflection_DeserializesSnakeCaseArraysBeyondInitialTypedBufferCapacity(int itemCount)
    {
        var options = new TomlSerializerOptions
        {
            PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower,
        };

        var result = TomlSerializer.Deserialize<Issue117ArrayHolder>(CreateItemsToml(itemCount), options);

        Assert.NotNull(result);
        Assert.Equal(GetExpectedIntItems(itemCount), result!.Items);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(33)]
    public void GeneratedContext_DeserializesSnakeCaseArraysBeyondInitialTypedBufferCapacity(int itemCount)
    {
        var result = TomlSerializer.Deserialize<Issue117ArrayHolder>(
            CreateItemsToml(itemCount),
            TestTomlSnakeCaseCollectionsContext.Default);

        Assert.NotNull(result);
        Assert.Equal(GetExpectedIntItems(itemCount), result!.Items);
    }

    [Fact]
    public void Default_DottedDictionaryKeys_RoundtripAsLiteral()
    {
        var context = TestTomlCollectionsContext.Default;
        var value = new DottedKeyDictionaryHolder
        {
            Map = new Dictionary<string, long> { ["a.b"] = 1 },
        };

        var toml = TomlSerializer.Serialize(value, context.DottedKeyDictionaryHolder);
        var model = TomlSerializer.Deserialize<TomlTable>(toml);

        Assert.NotNull(model);
        var nonNullModel = model!;
        Assert.True(nonNullModel.TryGetValue("map", out var mapValue));
        Assert.IsAssignableTo<TomlTable>(mapValue);
        var map = (TomlTable)mapValue!;

        Assert.True(map.TryGetValue("a.b", out var dottedValue));
        Assert.Equal(1L, dottedValue);

        var roundtrip = TomlSerializer.Deserialize(toml, context.DottedKeyDictionaryHolder);
        Assert.NotNull(roundtrip);
        Assert.Contains("a.b", roundtrip!.Map);
        Assert.Equal(1L, roundtrip.Map["a.b"]);
    }

    [Fact]
    public void Reflection_GetOnlyList_WithTomlSingleOrArray_CanReadSingleValue()
    {
        var result = TomlSerializer.Deserialize<SingleOrArrayCollectionHolder>("rid = \"test\"\n");

        Assert.NotNull(result);
        Assert.Equal(new[] { "test" }, result!.RuntimeIdentifiers);
    }

    [Fact]
    public void Reflection_GetOnlyList_WithTomlSingleOrArray_CanReadArray()
    {
        var result = TomlSerializer.Deserialize<SingleOrArrayCollectionHolder>("rid = [\"test1\", \"test2\"]\n");

        Assert.NotNull(result);
        Assert.Equal(new[] { "test1", "test2" }, result!.RuntimeIdentifiers);
    }

    [Fact]
    public void Reflection_SettableList_WithTomlSingleOrArray_ReplacesOnSingleValue()
    {
        var result = TomlSerializer.Deserialize<SingleOrArraySettableCollectionHolder>("rid = \"test\"\n");

        Assert.NotNull(result);
        Assert.Equal(new[] { "test" }, result!.RuntimeIdentifiers);
    }

    [Fact]
    public void Reflection_WithoutTomlSingleOrArray_SingleValueStillFails()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedCollectionHolder>("Values = 1\n"));

        Assert.Contains("Expected StartArray", ex!.Message);
    }

    [Theory]
    [InlineData("rid = \"test\"\n", "test")]
    [InlineData("rid = [\"test1\", \"test2\"]\n", "test1,test2")]
    public void ImmutableArray_WithTomlSingleOrArray_IsReadByBothResolvers(string toml, string expected)
    {
        var generated = TomlSerializer.Deserialize(toml, TestTomlCollectionsContext.Default.SingleOrArrayImmutableArrayHolder)!;
        var reflection = TomlSerializer.Deserialize<SingleOrArrayImmutableArrayHolder>(toml, new TomlSerializerOptions { PropertyNamingPolicy = TomlNamingPolicy.CamelCase })!;

        Assert.Equal(expected, string.Join(',', generated.Rid));
        Assert.Equal(expected, string.Join(',', reflection.Rid));
    }

    [Theory]
    [InlineData("rid = \"test\"\n", "test")]
    [InlineData("rid = [\"test1\", \"test2\"]\n", "test1,test2")]
    public void ConstructorBoundMembers_WithTomlSingleOrArray_AreReadByBothResolvers(string toml, string expected)
    {
        var options = new TomlSerializerOptions { PropertyNamingPolicy = TomlNamingPolicy.CamelCase };

        var generated = TomlSerializer.Deserialize(toml, TestTomlCollectionsContext.Default.SingleOrArrayConstructorArrayHolder)!;
        var reflection = TomlSerializer.Deserialize<SingleOrArrayConstructorArrayHolder>(toml, options)!;
        var generatedRecord = TomlSerializer.Deserialize(toml, TestTomlCollectionsContext.Default.SingleOrArrayRecordHolder)!;
        var reflectionRecord = TomlSerializer.Deserialize<SingleOrArrayRecordHolder>(toml, options)!;

        Assert.Equal(expected, string.Join(',', generated.Rid));
        Assert.Equal(expected, string.Join(',', reflection.Rid));
        Assert.Equal(expected, string.Join(',', generatedRecord.Rid));
        Assert.Equal(expected, string.Join(',', reflectionRecord.Rid));
    }

    // A get-only member without a collection cannot store a value, whether the TOML has an array or a single value
    [Theory]
    [InlineData("rid = [1, 2]\n")]
    [InlineData("rid = 1\n")]
    public void NullGetOnlyCollection_WithTomlSingleOrArray_IsAConfigurationError(string toml)
    {
        var options = new TomlSerializerOptions { PropertyNamingPolicy = TomlNamingPolicy.CamelCase };

        var generated = Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize<SingleOrArrayNullGetOnlyHolder>(toml, TestTomlCollectionsContext.Default, out _));
        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize<SingleOrArrayNullGetOnlyHolder>(toml, out _, options));

        Assert.True(reflection.IsConfigurationError);
        Assert.Equal(reflection.Message, generated.Message);
    }

    [Fact]
    public void GetOnlyImmutableCollection_WithTomlSingleOrArray_IsAConfigurationError()
    {
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize<SingleOrArrayGetOnlyImmutableHolder>("rid = 1\n", TestTomlCollectionsContext.Default, out _));

        Assert.True(exception.IsConfigurationError);
    }

    [Fact]
    public void GeneratedContext_GetOnlyList_WithTomlSingleOrArray_CanReadSingleValue()
    {
        var context = TestTomlCollectionsContext.Default;
        var result = TomlSerializer.Deserialize("rid = \"test\"\n", context.SingleOrArrayCollectionHolder);

        Assert.NotNull(result);
        Assert.Equal(new[] { "test" }, result!.RuntimeIdentifiers);
    }

    [Fact]
    public void GeneratedContext_GetOnlyList_WithTomlSingleOrArray_CanReadArray()
    {
        var context = TestTomlCollectionsContext.Default;
        var result = TomlSerializer.Deserialize("rid = [\"test1\", \"test2\"]\n", context.SingleOrArrayCollectionHolder);

        Assert.NotNull(result);
        Assert.Equal(new[] { "test1", "test2" }, result!.RuntimeIdentifiers);
    }

    [Fact]
    public void GeneratedContext_SettableList_WithTomlSingleOrArray_ReplacesOnSingleValue()
    {
        var context = TestTomlCollectionsContext.Default;
        var result = TomlSerializer.Deserialize("rid = \"test\"\n", context.SingleOrArraySettableCollectionHolder);

        Assert.NotNull(result);
        Assert.Equal(new[] { "test" }, result!.RuntimeIdentifiers);
    }

    [Fact]
    public void Reflection_ObservableCollection_WithTomlSingleOrArray_ReplacesOnSingleValue()
    {
        var result = TomlSerializer.Deserialize<SingleOrArrayObservableCollectionHolder>("Global = \"test\"\n");

        Assert.NotNull(result);
        Assert.IsAssignableTo<ObservableCollection<string>>(result!.Global);
        Assert.Equal(new[] { "test" }, result.Global);
    }

    [Fact]
    public void GeneratedContext_ObservableCollection_WithTomlSingleOrArray_ReplacesOnSingleValue()
    {
        var context = TestTomlCollectionsContext.Default;
        var result = TomlSerializer.Deserialize("global = \"test\"\n", context.SingleOrArrayObservableCollectionHolder);

        Assert.NotNull(result);
        Assert.IsAssignableTo<ObservableCollection<string>>(result!.Global);
        Assert.Equal(new[] { "test" }, result.Global);
    }

    private static string CreateItemsToml(int itemCount)
        => $"items = [{string.Join(",", Enumerable.Range(1, itemCount))}]";

    private static long[] GetExpectedLongItems(int itemCount)
        => Enumerable.Range(1, itemCount).Select(static value => (long)value).ToArray();

    private static int[] GetExpectedIntItems(int itemCount)
        => Enumerable.Range(1, itemCount).ToArray();
}
