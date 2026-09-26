using System;
using System.Collections.Generic;
using System.Linq;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public class SerializationTests
{
    [Fact]
    public void TestCrlfInMultilineString()
    {
        var store = new TomlMetadataStore();
        var options = new TomlSerializerOptions { MetadataStore = store };

        var model = new TomlTable();
        var metadata = new TomlPropertiesMetadata();
        metadata.SetProperty("property", new TomlPropertyMetadata
        {
            DisplayKind = TomlPropertyDisplayKind.StringLiteralMulti
        });
        store.SetProperties(model, metadata);

        model["property"] = "string\r\nwith\r\nnewlines";

        var result = TomlSerializer.Serialize(model, options).Trim();
        AssertHelper.AreEqualNormalizeNewLine("property = '''\nstring\r\nwith\r\nnewlines'''", result);
    }

    [Fact]
    public void TestArrayWithPrimitives()
    {
        var model = new TomlTable()
        {
            ["mixed-array"] = new TomlArray()
            {
                new TomlTable() { ["a"] = 1 },
                2,  // If instead the second or final item in the array was the table, it works as expected.
                3
            }
        };

        var result = TomlSerializer.Serialize(model).ReplaceLineEndings("\n").Trim();
        AssertHelper.AreEqualNormalizeNewLine("mixed-array = [{a = 1}, 2, 3]", result);
    }

    [Fact]
    public void TestNestedEmptyArrays()
    {
        var outer = new TomlTable
        {
            ["inner1"] = new TomlTable { },
            ["inner2"] = new TomlTable {
                { "array1", new TomlArray {} },
                { "array2", new TomlArray {} }
            },
            ["inner3"] = new TomlTable {
                { "array1", new TomlArray {} },
                { "array2", new TomlArray {} },
                { "array3", new TomlArray { "hello" } },
                { "array4", new TomlArray { } },
                { "array5", new TomlArray { } },
                { "array6", new TomlArray { } }
            },
            ["inner4"] = new TomlTable {
                { "array1", new TomlArray {} },
                { "string", "value" },
                { "array2", new TomlArray {} },
                { "array3", new TomlArray {} },
            },
        };

        var expecting = """
                        [inner1]
                        [inner2]
                        array1 = []
                        array2 = []
                        [inner3]
                        array1 = []
                        array2 = []
                        array3 = ["hello"]
                        array4 = []
                        array5 = []
                        array6 = []
                        [inner4]
                        array1 = []
                        string = "value"
                        array2 = []
                        array3 = []
                        """.ReplaceLineEndings("\n");

        var result = TomlSerializer.Serialize(outer).ReplaceLineEndings("\n").Trim();
        AssertHelper.AreEqualNormalizeNewLine(expecting, result);
    }

    [Theory]
    [InlineData(255L, TomlPropertyDisplayKind.IntegerHexadecimal, "0xff")]
    [InlineData(8L, TomlPropertyDisplayKind.IntegerOctal, "0o10")]
    [InlineData(5L, TomlPropertyDisplayKind.IntegerBinary, "0b101")]
    [InlineData(-1L, TomlPropertyDisplayKind.IntegerHexadecimal, "-1")]
    [InlineData(-8L, TomlPropertyDisplayKind.IntegerOctal, "-8")]
    [InlineData(long.MinValue, TomlPropertyDisplayKind.IntegerBinary, "-9223372036854775808")]
    public void Serialize_PreservedIntegerFormat_WritesValidToml(long value, TomlPropertyDisplayKind displayKind, string expected)
    {
        var store = new TomlMetadataStore();
        var model = new TomlTable { ["a"] = value };
        var metadata = new TomlPropertiesMetadata();
        metadata.SetProperty("a", new TomlPropertyMetadata { DisplayKind = displayKind });
        store.SetProperties(model, metadata);

        var toml = TomlSerializer.Serialize(model, new TomlSerializerOptions { MetadataStore = store });

        Assert.Equal("a = " + expected, toml.TrimEnd());
        Assert.Equal(value, TomlSerializer.Deserialize<TomlTable>(toml)!["a"]);
        Assert.Equal(expected, Helpers.TomlFormatHelper.ToString(value, displayKind));
    }

    [Fact]
    public void Serialize_DictionaryKeysMappedToTheSameName_Throws()
    {
        var options = new TomlSerializerOptions { DictionaryKeyPolicy = TomlNamingPolicy.CamelCase };
        var value = new Dictionary<string, int>(StringComparer.Ordinal) { ["Foo"] = 1, ["foo"] = 2 };

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(value, options));
        Assert.Contains("'foo' is written more than once", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_MembersMappedToTheSameName_Throws()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new CollidingMembers(), new TomlSerializerOptions { IncludeFields = true }));
    }

    [Fact]
    public void Serialize_ExpandedDottedKeyCollidingWithMember_Throws()
    {
        var options = new TomlSerializerOptions { DottedKeyHandling = TomlDottedKeyHandling.Expand };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new CollidingDottedMember(), options));
    }

    [Fact]
    public void Serialize_DottedKeyWithAnEmptySegment_IsWrittenLiterallyWithoutAnEmptyTable()
    {
        var options = new TomlSerializerOptions { DottedKeyHandling = TomlDottedKeyHandling.Expand };

        var toml = TomlSerializer.Serialize(new EmptySegmentDottedMembers(), options);

        Assert.Equal("\"a..b\" = 1\n\"c.\" = 2\n", toml);
    }

    [Fact]
    public void Serialize_ExpandedDottedKeyExtendingMemberTable_Merges()
    {
        var options = new TomlSerializerOptions { DottedKeyHandling = TomlDottedKeyHandling.Expand };

        var toml = TomlSerializer.Serialize(new ExtendingDottedMember(), options);

        var table = (TomlTable)TomlSerializer.Deserialize<TomlTable>(toml)!["A"];
        Assert.Equal(1L, table["X"]);
        Assert.Equal(2L, table["Y"]);
    }

    [Fact]
    public void Serialize_TomlTableMemberWithDottedKey_WritesTheKeyLiterally()
    {
        var options = new TomlSerializerOptions { DottedKeyHandling = TomlDottedKeyHandling.Expand };
        var value = new WithTable { Table = new TomlTable { ["a.b"] = 1L } };

        var toml = TomlSerializer.Serialize(value, options);

        Assert.Equal("[Table]\n\"a.b\" = 1\n", toml);
        Assert.Equal(toml, TomlSerializer.Serialize<object>(value, options));
        Assert.Equal(1L, TomlSerializer.Deserialize<WithTable>(toml, options)!.Table["a.b"]);
    }

    // xunit serializes the data of the test cases as UTF-8, which replaces a lone surrogate, so the text is built here
    [Theory]
    [InlineData(0xD800, "")]
    [InlineData(0xDC00, "x")]
    [InlineData(0xDBFF, "\"")]
    public void Serialize_UnpairedSurrogate_Throws(int surrogate, string suffix)
    {
        var text = "a" + (char)surrogate + suffix;

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new TomlTable { ["key"] = text }));
        Assert.Contains("unpaired surrogate", ex.Message, StringComparison.Ordinal);
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new TomlTable { [text] = 1L }));
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new GroupItem { Name = text }));
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new Dictionary<string, int>(StringComparer.Ordinal) { [text] = 1 }));
        Assert.Throws<ArgumentException>(() => new Syntax.StringValueSyntax(text));
    }

    [Fact]
    public void Serialize_SurrogatePair_RoundTrips()
    {
        var table = new TomlTable { ["\U0001F600"] = "a\U0001F600b" };

        var toml = TomlSerializer.Serialize(table);

        var roundtrip = TomlSerializer.Deserialize<TomlTable>(toml)!;
        Assert.Equal("a\U0001F600b", roundtrip["\U0001F600"]);
    }

    public static TheoryData<string> ClrValueNames() => new(
        "string", "bool", "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "nint", "nuint", "float", "float-nan",
        "double", "double-negative-zero", "decimal", "decimal-integer", "half", "int128", "uint128", "char", "guid", "timespan", "uri",
        "version", "enum", "toml-datetime", "datetime-utc", "datetime-local", "datetime-unspecified", "datetimeoffset", "dateonly",
        "timeonly", "poco", "list");

    private static object CreateClrValue(string name) => name switch
    {
        "string" => "a",
        "bool" => true,
        "sbyte" => (sbyte)-1,
        "byte" => (byte)1,
        "short" => (short)-2,
        "ushort" => (ushort)2,
        "int" => -3,
        "uint" => 3u,
        "long" => -4L,
        "ulong" => 4UL,
        "nint" => (nint)5,
        "nuint" => (nuint)5,
        "float" => 1.5f,
        "float-nan" => float.NaN,
        "double" => 1e300,
        "double-negative-zero" => -0.0,
        "decimal" => 1.25m,
        "decimal-integer" => 2m,
        "half" => (Half)1.5,
        "int128" => (Int128)6,
        "uint128" => (UInt128)6,
        "char" => 'c',
        "guid" => new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301"),
        "timespan" => TimeSpan.FromMinutes(90),
        "uri" => new Uri("https://example.com/"),
        "version" => new Version(1, 2, 3),
        "enum" => DayOfWeek.Monday,
        "toml-datetime" => new TomlDateTime(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), 0, TomlDateTimeKind.OffsetDateTimeByNumber),
        "datetime-utc" => new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        "datetime-local" => new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local),
        "datetime-unspecified" => new DateTime(2020, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified),
        "datetimeoffset" => new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(-7)),
        "dateonly" => new DateOnly(2020, 1, 2),
        "timeonly" => new TimeOnly(3, 4, 5),
        "poco" => new GroupItem { Name = "n" },
        "list" => new List<int> { 1, 2 },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>A <see cref="TomlTable"/> is written without the converters when it can be; the output must be the same.</summary>
    [Theory]
    [MemberData(nameof(ClrValueNames))]
    public void Serialize_TomlTableWithClrValue_WritesTheSameAsTheConverters(string name)
    {
        var table = new TomlTable
        {
            ["value"] = CreateClrValue(name),
            ["array"] = new TomlArray { CreateClrValue(name) },
            ["inline"] = new TomlTable(inline: true) { ["x"] = CreateClrValue(name) },
            ["table"] = new TomlTable { ["x"] = CreateClrValue(name) },
        };

        var direct = TomlSerializer.Serialize(table);

        // A metadata store makes the serializer write the table with the converters
        var withConverters = TomlSerializer.Serialize(table, new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() });
        Assert.Equal(withConverters, direct);
    }

    [Fact]
    public void Serialize_TomlTableWithCustomConverter_UsesTheConverter()
    {
        var options = new TomlSerializerOptions { Converters = [new UpperCaseStringConverter()] };

        var toml = TomlSerializer.Serialize(new TomlTable { ["value"] = "a" }, options);

        Assert.Equal("value = \"A\"\n", toml);
    }

    private sealed class UpperCaseStringConverter : TomlConverter<string>
    {
        public override string? Read(TomlReader reader) => reader.GetString();

        public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value.ToUpperInvariant());
    }

    [Fact]
    public void Serialize_WriteIndented_IndentsNestedTables()
    {
        var table = new TomlTable
        {
            ["title"] = "x",
            ["a"] = new TomlTable
            {
                ["k"] = 1L,
                ["b"] = new TomlTable { ["text"] = "line1\nline2", ["c"] = new TomlTable { ["x"] = new TomlArray { 1L, 2L } } },
                ["items"] = new TomlTableArray { new TomlTable { ["n"] = 1L }, new TomlTable { ["n"] = 2L } },
            },
        };
        var options = new TomlSerializerOptions { WriteIndented = true, IndentSize = 4, MetadataStore = new TomlMetadataStore() };
        var metadata = new TomlPropertiesMetadata();
        metadata.SetProperty("text", new TomlPropertyMetadata { DisplayKind = TomlPropertyDisplayKind.StringMulti });
        options.MetadataStore!.SetProperties((TomlTable)((TomlTable)table["a"])["b"], metadata);

        var toml = TomlSerializer.Serialize(table, options);

        Assert.Equal(""""
            title = "x"
            [a]
            k = 1
                [a.b]
                text = """
            line1
            line2"""
                    [a.b.c]
                    x = [1, 2]
                [[a.items]]
                n = 1

                [[a.items]]
                n = 2

            """".ReplaceLineEndings("\n"), toml);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(ModelHelper.ToJson(table), ModelHelper.ToJson(TomlSerializer.Deserialize<TomlTable>(toml))));
    }

    [Fact]
    public void Serialize_WriteIndentedIsFalseByDefault()
    {
        var table = new TomlTable { ["a"] = new TomlTable { ["b"] = new TomlTable { ["x"] = 1L } } };

        Assert.False(TomlSerializerOptions.Default.WriteIndented);
        Assert.Equal("[a]\n[a.b]\nx = 1\n", TomlSerializer.Serialize(table));
    }

    [Fact]
    public void Serialize_EmptyTomlTableArray_IsWrittenAsAnEmptyArray()
    {
        var table = new TomlTable { ["items"] = new TomlTableArray(), ["t"] = new TomlTable { ["nested"] = new TomlTableArray() } };

        var toml = TomlSerializer.Serialize(table);

        Assert.Equal("items = []\n[t]\nnested = []\n", toml);
        Assert.Equal(toml, TomlSerializer.Serialize(table, new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() }));
        var roundtrip = TomlSerializer.Deserialize<TomlTable>(toml)!;
        Assert.Empty((TomlArray)roundtrip["items"]);
        Assert.Empty((TomlArray)((TomlTable)roundtrip["t"])["nested"]);
    }

    [Fact]
    public void Serialize_PropertyHiddenWithNew_UsesTheDerivedProperty()
    {
        var toml = TomlSerializer.Serialize(new HidingDerived());

        // Base type members come first, and each hidden member is replaced by the one of the derived type
        Assert.Equal("Other = 2\nId = \"d\"\n", toml);
        Assert.Equal("Other = 2\nField = \"f\"\nId = \"d\"\n", TomlSerializer.Serialize(new HidingDerived(), new TomlSerializerOptions { IncludeFields = true }));
        Assert.Equal("abc", TomlSerializer.Deserialize<HidingDerived>("Id = 'abc'")!.Id);
        Assert.Equal("x", TomlSerializer.Deserialize<HidingDerived>("Field = 'x'", new TomlSerializerOptions { IncludeFields = true })!.Field);
    }

    private class HidingBase
    {
        public int Id { get; set; } = 1;

        public int Other { get; set; } = 2;

#pragma warning disable CA1051 // The test needs public fields
        public int Field = 3;
#pragma warning restore CA1051
    }

    private sealed class HidingDerived : HidingBase
    {
        public new string Id { get; set; } = "d";

#pragma warning disable CA1051 // The test needs public fields
        public new string? Field = "f";
#pragma warning restore CA1051
    }

    private sealed class WithTable
    {
        public TomlTable Table { get; set; } = [];
    }

    [Fact]
    public void DottedKeyHandlingExpand_OnlyAffectsWriting()
    {
        var options = new TomlSerializerOptions { DottedKeyHandling = TomlDottedKeyHandling.Expand };
        var value = new Dictionary<string, string>(StringComparer.Ordinal) { ["a.b"] = "x" };

        var toml = TomlSerializer.Serialize(value, options);

        Assert.Equal("[a]\nb = \"x\"\n", toml);
        Assert.Equal("x", ((TomlTable)TomlSerializer.Deserialize<TomlTable>(toml, options)!["a"])["b"]);
        Assert.Equal("x", TomlSerializer.Deserialize<Dictionary<string, string>>("\"a.b\" = \"x\"", options)!["a.b"]);
    }

    private sealed class CollidingMembers
    {
        public int A { get; set; } = 1;

        [TomlPropertyName("A")]
#pragma warning disable CA1051 // The test needs a public field
        public int B = 2;
#pragma warning restore CA1051
    }

    private sealed class NestedX
    {
        public int X { get; set; } = 1;
    }

    private sealed class EmptySegmentDottedMembers
    {
        [TomlPropertyName("a..b")]
        public int First { get; set; } = 1;

        [TomlPropertyName("c.")]
        public int Second { get; set; } = 2;
    }

    private sealed class CollidingDottedMember
    {
        [TomlPropertyName("A.X")]
        public int First { get; set; } = 2;

        public NestedX A { get; set; } = new();
    }

    private sealed class ExtendingDottedMember
    {
        [TomlPropertyName("A.Y")]
        public int First { get; set; } = 2;

        public NestedX A { get; set; } = new();
    }

    [Fact]
    public void Serialize_TableArrayInsideArray_IsWrittenInline()
    {
        var value = new NestedGroups { Groups = [[new GroupItem { Name = "n", Children = [new GroupItem { Name = "c" }] }]] };

        var toml = TomlSerializer.Serialize(value);

        Assert.Equal("Groups = [[{Name = \"n\", Children = [{Name = \"c\", Children = []}]}]]\n", toml);
        var roundtrip = TomlSerializer.Deserialize<NestedGroups>(toml)!;
        Assert.Equal("c", roundtrip.Groups[0][0].Children[0].Name);
    }

    [Fact]
    public void Serialize_TableArrayInsideInlineTable_IsWrittenInline()
    {
        var table = new TomlTable { ["t"] = new TomlTable(inline: true) { ["children"] = new TomlTableArray { new TomlTable { ["name"] = "c", ["sub"] = new TomlTable { ["x"] = 1L } } } } };

        var toml = TomlSerializer.Serialize(table);

        Assert.Equal("t = {children = [{name = \"c\", sub = {x = 1}}]}\n", toml);
        Assert.NotNull(TomlSerializer.Deserialize<TomlTable>(toml));
    }

    private sealed class NestedGroups
    {
        public List<List<GroupItem>> Groups { get; set; } = [];
    }

    private sealed class GroupItem
    {
        public string Name { get; set; } = "";

        public List<GroupItem> Children { get; set; } = [];
    }

    [Fact]
    public void Serialize_ObjectValuesWithRuntimeTypes_UsesTheirMetadata()
    {
        var value = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["list"] = new List<int> { 1, 2 },
            ["array"] = new[] { "a", "b" },
            ["poco"] = new GroupItem { Name = "n" },
            ["items"] = new List<GroupItem> { new() { Name = "x" } },
        };

        var table = TomlSerializer.Deserialize<TomlTable>(TomlSerializer.Serialize(value))!;

        Assert.Equal([1L, 2L], ((TomlArray)table["list"]).Cast<long>());
        Assert.Equal(["a", "b"], ((TomlArray)table["array"]).Cast<string>());
        Assert.Equal("n", ((TomlTable)table["poco"])["Name"]);
        Assert.Equal("x", ((TomlTableArray)table["items"])[0]["Name"]);
        Assert.Contains("Name = \"n\"", TomlSerializer.Serialize<object>(new GroupItem { Name = "n" }), StringComparison.Ordinal);
    }
}
