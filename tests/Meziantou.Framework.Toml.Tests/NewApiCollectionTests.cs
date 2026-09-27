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

// Every supported collection type, in both resolvers: written, read, read from a single value, and populated
public sealed class CollectionMatrixHolder<TCollection>
{
    public TCollection? V { get; set; }
}

public sealed class CollectionMatrixSingleOrArrayHolder<TCollection>
{
    [TomlSingleOrArray]
    public TCollection? V { get; set; }
}

public sealed class CollectionMatrixPopulateHolder<TCollection>
    where TCollection : class, ICollection<int>, new()
{
    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public TCollection V { get; } = CollectionMatrix.Create<TCollection>();
}

public sealed class CollectionMatrixSingleOrArrayPopulateHolder<TCollection>
    where TCollection : class, ICollection<int>, new()
{
    [TomlSingleOrArray]
    public TCollection V { get; } = CollectionMatrix.Create<TCollection>();
}

// Members whose type is an interface, populated through the existing collection
public sealed class CollectionMatrixInterfacePopulateHolder
{
    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public IList<int> List { get; } = new List<int> { 0 };

    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public ICollection<int> Collection { get; } = new Collection<int> { 0 };

    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public ISet<int> Set { get; } = new HashSet<int> { 0 };

    [TomlSingleOrArray]
    public ISet<int> SingleOrArraySet { get; } = new HashSet<int> { 0 };

    [TomlSingleOrArray]
    public IList<int> SingleOrArrayList { get; } = new List<int> { 0 };
}

public static class CollectionMatrix
{
    public static TCollection Create<TCollection>()
        where TCollection : ICollection<int>, new()
    {
        return [0];
    }
}

[TomlSerializable(typeof(CollectionMatrixHolder<int[]>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<int[]>))]
[TomlSerializable(typeof(CollectionMatrixHolder<List<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<List<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<IList<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<IList<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<IReadOnlyList<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<IReadOnlyList<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<ICollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<ICollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<IReadOnlyCollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<IReadOnlyCollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<IEnumerable<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<IEnumerable<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<Collection<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<Collection<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<ObservableCollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<ObservableCollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<HashSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<HashSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<SortedSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<SortedSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<ISet<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<ISet<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<IReadOnlySet<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<IReadOnlySet<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<System.Collections.Immutable.ImmutableArray<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<System.Collections.Immutable.ImmutableArray<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<System.Collections.Immutable.ImmutableList<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<System.Collections.Immutable.ImmutableList<int>>))]
[TomlSerializable(typeof(CollectionMatrixHolder<System.Collections.Immutable.ImmutableHashSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayHolder<System.Collections.Immutable.ImmutableHashSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixPopulateHolder<List<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayPopulateHolder<List<int>>))]
[TomlSerializable(typeof(CollectionMatrixPopulateHolder<Collection<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayPopulateHolder<Collection<int>>))]
[TomlSerializable(typeof(CollectionMatrixPopulateHolder<ObservableCollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayPopulateHolder<ObservableCollection<int>>))]
[TomlSerializable(typeof(CollectionMatrixPopulateHolder<HashSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayPopulateHolder<HashSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixPopulateHolder<SortedSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixSingleOrArrayPopulateHolder<SortedSet<int>>))]
[TomlSerializable(typeof(CollectionMatrixInterfacePopulateHolder))]
internal sealed partial class TestTomlCollectionMatrixContext : TomlSerializerContext
{
}

public class NewApiCollectionTests
{
    [Fact]
    public void CollectionMatrix_EveryCollectionIsWrittenAndReadByBothResolvers()
    {
        Check<int[]>();
        Check<List<int>>();
        Check<IList<int>>();
        Check<IReadOnlyList<int>>();
        Check<ICollection<int>>();
        Check<IReadOnlyCollection<int>>();
        Check<IEnumerable<int>>();
        Check<Collection<int>>();
        Check<ObservableCollection<int>>();
        Check<HashSet<int>>();
        Check<SortedSet<int>>();
        Check<ISet<int>>();
        Check<IReadOnlySet<int>>();
        Check<System.Collections.Immutable.ImmutableArray<int>>();
        Check<System.Collections.Immutable.ImmutableList<int>>();
        Check<System.Collections.Immutable.ImmutableHashSet<int>>();

        static void Check<TCollection>()
            where TCollection : IEnumerable<int>
        {
            var context = TestTomlCollectionMatrixContext.Default;
            var name = typeof(TCollection).Name;

            var generated = TomlSerializer.Deserialize<CollectionMatrixHolder<TCollection>>("V = [3, 1, 2]\n", context)!;
            var reflection = TomlSerializer.Deserialize<CollectionMatrixHolder<TCollection>>("V = [3, 1, 2]\n")!;
            Assert.Equal([1, 2, 3], generated.V!.Order(), name);
            Assert.Equal([1, 2, 3], reflection.V!.Order(), name);

            var written = TomlSerializer.Serialize(generated, context);
            Assert.Equal(written, TomlSerializer.Serialize(generated), name);
            Assert.Equal([1, 2, 3], TomlSerializer.Deserialize<CollectionMatrixHolder<TCollection>>(written, context)!.V!.Order(), name);

            foreach (var (toml, expected) in new[] { ("V = 1\n", new[] { 1 }), ("V = [1, 2]\n", new[] { 1, 2 }) })
            {
                Assert.Equal(expected, TomlSerializer.Deserialize<CollectionMatrixSingleOrArrayHolder<TCollection>>(toml, context)!.V!.Order(), name);
                Assert.Equal(expected, TomlSerializer.Deserialize<CollectionMatrixSingleOrArrayHolder<TCollection>>(toml)!.V!.Order(), name);
            }
        }
    }

    [Fact]
    public void CollectionMatrix_EveryMutableCollectionIsPopulatedByBothResolvers()
    {
        Check<List<int>>();
        Check<Collection<int>>();
        Check<ObservableCollection<int>>();
        Check<HashSet<int>>();
        Check<SortedSet<int>>();

        var context = TestTomlCollectionMatrixContext.Default;
        const string InterfaceToml = "List = [1]\nCollection = [1]\nSet = [1]\nSingleOrArraySet = 1\nSingleOrArrayList = [1, 2]\n";
        foreach (var holder in new[] { TomlSerializer.Deserialize<CollectionMatrixInterfacePopulateHolder>(InterfaceToml, context)!, TomlSerializer.Deserialize<CollectionMatrixInterfacePopulateHolder>(InterfaceToml)! })
        {
            Assert.Equal([0, 1], holder.List);
            Assert.Equal([0, 1], holder.Collection);
            Assert.Equal([0, 1], holder.Set.Order());
            Assert.Equal([0, 1], holder.SingleOrArraySet.Order());
            Assert.Equal([0, 1, 2], holder.SingleOrArrayList);
        }

        static void Check<TCollection>()
            where TCollection : class, ICollection<int>, new()
        {
            var context = TestTomlCollectionMatrixContext.Default;
            var name = typeof(TCollection).Name;

            Assert.Equal([0, 1, 2], TomlSerializer.Deserialize<CollectionMatrixPopulateHolder<TCollection>>("V = [1, 2]\n", context)!.V.Order(), name);
            Assert.Equal([0, 1, 2], TomlSerializer.Deserialize<CollectionMatrixPopulateHolder<TCollection>>("V = [1, 2]\n")!.V.Order(), name);
            foreach (var (toml, expected) in new[] { ("V = 1\n", new[] { 0, 1 }), ("V = [1, 2]\n", new[] { 0, 1, 2 }) })
            {
                Assert.Equal(expected, TomlSerializer.Deserialize<CollectionMatrixSingleOrArrayPopulateHolder<TCollection>>(toml, context)!.V.Order(), name);
                Assert.Equal(expected, TomlSerializer.Deserialize<CollectionMatrixSingleOrArrayPopulateHolder<TCollection>>(toml)!.V.Order(), name);
            }
        }
    }

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
