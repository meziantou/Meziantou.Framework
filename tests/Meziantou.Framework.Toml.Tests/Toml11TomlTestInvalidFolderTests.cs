using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Tomlyn.Tests;

public sealed class Toml11TomlTestInvalidFolderTests
{
    [Theory]
    [MemberData(nameof(ListToml11InvalidFolderExtensions))]
    public static void Toml11Extensions_SyntaxParser_Roundtrips(string name)
    {
        var (inputName, toml, _) = StandardTests.GetCase(name);
        var doc = SyntaxParser.Parse(toml, inputName);
        var roundtrip = doc.ToString();

        if (doc.HasErrors || toml != roundtrip)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Testing {inputName}");
            StandardTests.Dump(toml, doc, roundtrip);
        }

        Assert.False(doc.HasErrors, message: "TOML 1.1 extension should parse without errors.");
        Assert.Equal(toml, roundtrip, message: "Syntax roundtrip should preserve input for full fidelity.");

        using var reader = new StringReader(toml);
        var docFromReader = SyntaxParser.Parse(reader, inputName);
        Assert.False(docFromReader.HasErrors, message: "TextReader input should parse without errors.");
        Assert.Equal(roundtrip, docFromReader.ToString(), message: "TextReader and string syntax roundtrips must match.");
    }

    [Theory]
    [MemberData(nameof(ListToml11InvalidFolderExtensions))]
    public static void Toml11Extensions_UntypedModel_RoundtripsSemantics(string name)
    {
        var (inputName, toml, _) = StandardTests.GetCase(name);
        var model = TomlSerializer.Deserialize<TomlTable>(toml)!;
        var tomlFromModel = TomlSerializer.Serialize(model);
        var model2 = TomlSerializer.Deserialize<TomlTable>(tomlFromModel)!;

        var json1 = ModelHelper.ToJson(model);
        var json2 = ModelHelper.ToJson(model2);
        Assert.True(JsonNode.DeepEquals(json1, json2), message: $"Untyped model must roundtrip semantics for {inputName}.");
    }

    [Fact]
    public static void MinuteOnlyOffsetDateTime_DeserializesToTomlDateTime()
    {
        var model = TomlSerializer.Deserialize<TomlTable>("no-secs = 1987-07-05T17:45Z")!;
        Assert.True(model.TryGetValue("no-secs", out var raw));
        Assert.NotNull(raw);
        var value = (TomlDateTime)raw!;

        Assert.Equal(TomlDateTimeKind.OffsetDateTimeByZ, value.Kind);
        Assert.Equal(17, value.DateTime.UtcDateTime.Hour);
        Assert.Equal(45, value.DateTime.UtcDateTime.Minute);
        Assert.Equal(0, value.DateTime.UtcDateTime.Second);
    }

    [Fact]
    public static void MinuteOnlyLocalDateTime_DeserializesToTomlDateTime()
    {
        var model = TomlSerializer.Deserialize<TomlTable>("no-secs = 1987-07-05T17:45")!;
        Assert.True(model.TryGetValue("no-secs", out var raw));
        Assert.NotNull(raw);
        var value = (TomlDateTime)raw!;

        Assert.Equal(TomlDateTimeKind.LocalDateTime, value.Kind);
        Assert.Equal(17, value.DateTime.DateTime.Hour);
        Assert.Equal(45, value.DateTime.DateTime.Minute);
        Assert.Equal(0, value.DateTime.DateTime.Second);
    }

    [Fact]
    public static void MinuteOnlyLocalTime_DeserializesToTomlDateTime()
    {
        var model = TomlSerializer.Deserialize<TomlTable>("no-secs = 17:45")!;
        Assert.True(model.TryGetValue("no-secs", out var raw));
        Assert.NotNull(raw);
        var value = (TomlDateTime)raw!;

        Assert.Equal(TomlDateTimeKind.LocalTime, value.Kind);
        Assert.Equal(17, value.DateTime.DateTime.Hour);
        Assert.Equal(45, value.DateTime.DateTime.Minute);
        Assert.Equal(0, value.DateTime.DateTime.Second);
    }

    [Fact]
    public static void BasicString_HexByteEscape_Deserializes()
    {
        var model = TomlSerializer.Deserialize<TomlTable>("answer = \"\\x33\"")!;
        Assert.True(model.TryGetValue("answer", out var raw));
        Assert.NotNull(raw);
        Assert.Equal("3", (string)raw!);
    }

    [Fact]
    public static void InlineTable_TrailingComma_Deserializes()
    {
        var model = TomlSerializer.Deserialize<TomlTable>("abc = { abc = 123, }")!;
        Assert.True(model.TryGetValue("abc", out var raw));
        Assert.NotNull(raw);
        var inline = (TomlTable)raw!;
        Assert.Equal(123L, (long)inline["abc"]);
    }

    [Fact]
    public static void InlineTable_Multiline_Deserializes()
    {
        var model = TomlSerializer.Deserialize<TomlTable>("t = {a=1,\n b=2}\n")!;
        Assert.True(model.TryGetValue("t", out var raw));
        Assert.NotNull(raw);
        var inline = (TomlTable)raw!;
        Assert.Equal(1L, (long)inline["a"]);
        Assert.Equal(2L, (long)inline["b"]);
    }

    public static TheoryData<string> ListToml11InvalidFolderExtensions() => new(Toml11ValidButTomlTestMarksInvalid);

    private static readonly string[] Toml11ValidButTomlTestMarksInvalid =
    [
        "invalid/datetime/no-secs",
        "invalid/local-datetime/no-secs",
        "invalid/local-time/no-secs",
        "invalid/string/basic-byte-escapes",
        "invalid/inline-table/trailing-comma",
        "invalid/inline-table/linebreak-01",
        "invalid/inline-table/linebreak-02",
        "invalid/inline-table/linebreak-03",
        "invalid/inline-table/linebreak-04",
    ];
}
