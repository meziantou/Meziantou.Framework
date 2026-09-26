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

    private sealed class WithTable
    {
        public TomlTable Table { get; set; } = [];
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
