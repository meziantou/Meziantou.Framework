using System;
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
}
