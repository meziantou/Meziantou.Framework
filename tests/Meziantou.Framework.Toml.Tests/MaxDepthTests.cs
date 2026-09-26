using System;
using System.IO;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class MaxDepthTests
{
    private const int DefaultMaxDepth = 64;

    [Fact]
    public void TomlSerializerOptions_NegativeMaxDepth_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                MaxDepth = -1,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void Deserialize_DeeplyNestedArrays_UsesDefaultMaxDepth()
    {
        var toml = CreateNestedArrayToml(DefaultMaxDepth);

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<object>(toml));

        Assert.Contains($"maximum depth of {DefaultMaxDepth}", ex!.Message);
    }

    [Fact]
    public void Deserialize_DeeplyNestedArrays_AllowsCustomMaxDepth()
    {
        var toml = CreateNestedArrayToml(8);
        var options = TomlSerializerOptions.Default with { MaxDepth = 16 };

        var result = TomlSerializer.Deserialize<object>(toml, options);

        Assert.IsType<TomlTable>(result);
        var root = (TomlTable)result!;
        Assert.Equal(8, GetNestedArrayDepth((TomlArray)root["value"]!));
    }

    [Fact]
    public void TomlParser_MoveNext_RespectsMaxDepth()
    {
        var parser = TomlParser.Create("value = [[1]]", TomlSerializerOptions.Default with { MaxDepth = 2 });

        var ex = Assert.Throws<TomlException>(() =>
        {
            while (parser.MoveNext())
            {
            }
        });

        Assert.Contains("maximum depth of 2", ex!.Message);
    }

    [Fact]
    public void SyntaxParser_ParseStrict_RespectsMaxDepth()
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = 2 };

        var ex = Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict("value = [[1]]", options));

        Assert.Contains("maximum depth of 2", ex!.Message);
    }

    [Fact]
    public void TomlWriter_WriteStartArray_RespectsMaxDepth()
    {
        using var textWriter = new StringWriter();
        var writer = new TomlWriter(textWriter, TomlSerializerOptions.Default with { MaxDepth = 1 });
        writer.WriteStartDocument();
        writer.WriteStartTable();
        writer.WritePropertyName("value");

        var ex = Assert.Throws<TomlException>(() => writer.WriteStartArray());

        Assert.Contains("maximum depth of 1", ex!.Message);
    }

    [Fact]
    public void Serialize_TomlTableFastPath_RespectsMaxDepth()
    {
        var root = CreateNestedTableChain(depth: 3);
        var options = TomlSerializerOptions.Default with { MaxDepth = 2 };

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(root, options));

        Assert.Contains("maximum depth of 2", ex!.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SyntaxParser_LongDottedKey_RespectsMaxDepth(bool isTableHeader)
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = 8 };
        var key = string.Join('.', Enumerable.Repeat("a", isTableHeader ? 8 : 9));
        var toml = isTableHeader ? $"[{key}]\n" : $"{key} = 1\n";

        var ex = Assert.Throws<TomlException>(() => SyntaxParser.Parse(toml, options));
        Assert.Contains("maximum depth of 8", ex.Message, StringComparison.Ordinal);
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml, options));
    }

    [Theory]
    [InlineData("[a.a.a.a.a.a.a]\n")]
    [InlineData("a.a.a.a.a.a.a.a = 1\n")]
    public void SyntaxParser_DottedKeyAtMaxDepth_IsAccepted(string toml)
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = 8 };

        Assert.False(SyntaxParser.Parse(toml, options).HasErrors);
        Assert.NotNull(TomlSerializer.Deserialize<TomlTable>(toml, options));
    }

    [Fact]
    public void SyntaxParser_HeadersWithLongSharedPrefix_AreRejectedQuickly()
    {
        var prefix = string.Join('.', Enumerable.Repeat("a", 20_000));
        var toml = $"[{prefix}.b]\n[{prefix}.c]\n";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<TomlException>(() => SyntaxParser.Parse(toml));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Parsing took {stopwatch.Elapsed}");
    }

    private static string CreateNestedArrayToml(int arrayDepth)
    {
        return $"value = {new string('[', arrayDepth)}1{new string(']', arrayDepth)}";
    }

    private static int GetNestedArrayDepth(TomlArray array)
    {
        var depth = 1;
        var current = array;
        while (current.Count > 0 && current[0] is TomlArray nested)
        {
            depth++;
            current = nested;
        }

        return depth;
    }

    private static TomlTable CreateNestedTableChain(int depth)
    {
        var root = new TomlTable();
        var current = root;
        for (var i = 1; i < depth; i++)
        {
            var child = new TomlTable();
            current[$"level{i}"] = child;
            current = child;
        }

        current["value"] = 1L;
        return root;
    }
}
