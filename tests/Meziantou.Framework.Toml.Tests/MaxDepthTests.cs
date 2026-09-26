using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Syntax;

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

    [Theory]
    [InlineData("value = [[[1]]]")]
    [InlineData("a.b.c = 1")]
    [InlineData("[a.b.c]")]
    public void TomlParser_TolerantMode_ReportsMaxDepthWithoutThrowing(string toml)
    {
        var parser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant }, TomlSerializerOptions.Default with { MaxDepth = 2 });

        while (parser.MoveNext())
        {
        }

        Assert.True(parser.HasErrors);
        Assert.Contains(parser.Diagnostics, diagnostic => diagnostic.Message.Contains("maximum depth of 2", StringComparison.Ordinal));
        Assert.False(parser.MoveNext());
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

        var doc = SyntaxParser.Parse(toml, options);
        Assert.Contains(doc.Diagnostics, diagnostic => diagnostic.Message.Contains("maximum depth of 8", StringComparison.Ordinal));
        Assert.Equal(toml, doc.ToString());
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml, options));
    }

    [Theory]
    [InlineData("value = [[1]]\nother = 2\n")]
    [InlineData("value = {a = {b = 1}}\n[t]\nx = 1\n")]
    [InlineData("value = [{a = [1, 2]}]")]
    public void SyntaxParser_DeepContainer_ReportsADiagnostic(string toml)
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = 2 };

        var doc = SyntaxParser.Parse(toml, options);

        Assert.Contains(doc.Diagnostics, diagnostic => diagnostic.Message.Contains("maximum depth of 2", StringComparison.Ordinal));
        Assert.Equal(toml, doc.ToString());
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

    [Theory]
    [InlineData("[[a.a.a]]\n")]
    [InlineData("[a.a.a]\nx = [[[]]]\n")]
    [InlineData("[[a]]\n[a.b]\n")]
    [InlineData("[[\"a\"]]\n[a.b.c]\nx = 1\n")]
    [InlineData("[[a]]\n[[a.b]]\n[a.b.c]\ny.z = [1]\n")]
    [InlineData("[[a]]\n[[a]]\n[a.b.c]\n")]
    [InlineData("[a]\nb.c.d = {e = [1]}\n")]
    [InlineData("a.b.c = [{d.e = [1]}]\n")]
    [InlineData("x = {a.b = {c = [[1]]}}\n")]
    public void SyntaxParser_CountsDepthLikeTheDeserializer(string toml)
    {
        for (var maxDepth = 1; maxDepth <= 12; maxDepth++)
        {
            var options = TomlSerializerOptions.Default with { MaxDepth = maxDepth };
            var deserializerFails = false;
            try
            {
                TomlSerializer.Deserialize<TomlTable>(toml, options);
            }
            catch (TomlException)
            {
                deserializerFails = true;
            }

            var doc = SyntaxParser.Parse(toml, options);
            Assert.Equal(deserializerFails, doc.HasErrors, message: $"MaxDepth = {maxDepth}: {doc.Diagnostics}");
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(30_000)]
    public void SyntaxParser_DeepContainer_ReportsOnlyTheDepthError(int depth)
    {
        foreach (var toml in new[] { CreateNestedArrayToml(depth), $"value = {string.Concat(Enumerable.Repeat("{a = ", depth))}1{new string('}', depth)}" })
        {
            var doc = SyntaxParser.Parse(toml);

            var diagnostic = Assert.Single(doc.Diagnostics);
            Assert.Contains("maximum depth of 64", diagnostic.Message);
            Assert.Equal(toml, doc.ToString());
        }
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(62, false)]
    public void SyntaxParser_TableArrayHeaderCountsTheElement(int segments, bool isError)
    {
        var toml = $"[[{string.Join('.', Enumerable.Repeat("a", segments))}]]\n";

        Assert.Equal(isError, SyntaxParser.Parse(toml).HasErrors);
    }

    [Fact]
    public void SyntaxParser_HeadersWithLongSharedPrefix_AreRejectedQuickly()
    {
        var prefix = string.Join('.', Enumerable.Repeat("a", 20_000));
        var toml = $"[{prefix}.b]\n[{prefix}.c]\n";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(SyntaxParser.Parse(toml).HasErrors);
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

    [Fact]
    public void Deserialize_UnlimitedMaxDepth_ThrowsBeforeTheStackOverflows()
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = int.MaxValue };
        var toml = CreateNestedArrayToml(100_000);

        AssertNestedTooDeeply(RunWithSmallStack(() => TomlSerializer.Deserialize<TomlTable>(toml, options)));
        AssertNestedTooDeeply(RunWithSmallStack(() => TomlSerializer.Deserialize<object>(toml, options)));
    }

    [Fact]
    public void SyntaxParser_UnlimitedMaxDepth_ReportsADiagnosticBeforeTheStackOverflows()
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = int.MaxValue };
        var toml = CreateNestedArrayToml(100_000);
        DocumentSyntax? doc = null;

        Assert.Null(RunWithSmallStack(() => doc = SyntaxParser.Parse(toml, options)));

        Assert.Contains(doc!.Diagnostics, diagnostic => diagnostic.Message.Contains("nested too deeply", StringComparison.Ordinal));
        Assert.Equal(toml, doc.ToString());
    }

    [Theory]
    [InlineData(1_000, false)]
    [InlineData(3_000, false)]
    [InlineData(10_000, false)]
    [InlineData(30_000, false)]
    [InlineData(1_000, true)]
    [InlineData(10_000, true)]
    public void SyntaxParser_UnlimitedMaxDepth_ValidatesAndWritesDeepTreesWithoutOverflowing(int depth, bool inlineTables)
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = int.MaxValue };
        var toml = inlineTables
            ? "a = " + string.Concat(Enumerable.Repeat("{b = ", depth)) + "1" + new string('}', depth)
            : CreateNestedArrayToml(depth);
        string? text = null;

        Assert.Null(RunWithSmallStack(() => text = SyntaxParser.Parse(toml, options).ToString()));

        Assert.Equal(toml, text);
    }

    [Fact]
    public void Serialize_UnlimitedMaxDepth_ThrowsBeforeTheStackOverflows()
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = int.MaxValue };
        var model = new TomlArray();
        var list = new List<object>();
        var currentModel = model;
        var currentList = list;
        for (var i = 0; i < 100_000; i++)
        {
            var nextModel = new TomlArray();
            currentModel.Add(nextModel);
            currentModel = nextModel;

            var nextList = new List<object>();
            currentList.Add(nextList);
            currentList = nextList;
        }

        AssertNestedTooDeeply(RunWithSmallStack(() => TomlSerializer.Serialize(new TomlTable { ["value"] = model }, options)));
        AssertNestedTooDeeply(RunWithSmallStack(() => TomlSerializer.Serialize(new Dictionary<string, object> { ["value"] = list }, options)));
    }

    private static void AssertNestedTooDeeply(Exception? exception)
    {
        var tomlException = Assert.IsType<TomlException>(exception);
        Assert.Contains("nested too deeply", tomlException.Message, StringComparison.Ordinal);
    }

    // The default stack of a thread differs between platforms, so the tests choose one
    // A regression would loop forever, so the serialization runs on a thread the test does not wait for indefinitely
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(int.MaxValue, false)]
    public void Serialize_DomThatContainsItself_ThrowsInsteadOfLoopingForever(int maxDepth, bool isArray)
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = maxDepth };
        var table = new TomlTable();
        if (isArray)
        {
            var array = new TomlArray();
            array.Add(array);
            table["a"] = array;
        }
        else
        {
            table["self"] = table;
        }

        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                TomlSerializer.Serialize(table, options);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        }, maxStackSize: 512 * 1024)
        {
            IsBackground = true,
        };
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Serialize did not complete");
        Assert.IsType<TomlException>(exception);
    }

    private static Exception? RunWithSmallStack(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        }, maxStackSize: 512 * 1024);
        thread.Start();
        thread.Join();
        return exception;
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
