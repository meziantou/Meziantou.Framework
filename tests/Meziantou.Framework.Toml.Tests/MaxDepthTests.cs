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
    [InlineData("[[b]]\n[x]\n[[b.a.c]]\n")]
    [InlineData("[[a]]\n[x]\n[a.b]\n")]
    [InlineData("[[a]]\n[[a.b]]\n[x]\n[[a.b.c]]\n")]
    [InlineData("[[a]]\n[[a.b]]\n[a.y]\n[[a.b.c]]\n")]
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
            if (!deserializerFails)
            {
                Assert.True(GetModelDepth(TomlSerializer.Deserialize<TomlTable>(toml, options)!) <= maxDepth, $"MaxDepth = {maxDepth}");
            }
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
    [InlineData("a.b.c = 1\n", 2)]
    [InlineData("[a.b.c]\n", 2)]
    [InlineData("[[a]]\n[[a.b]]\n", 3)]
    [InlineData("x = {a.b.c = 1}\n", 3)]
    public void SyntaxParser_ReportsDepthErrorsWhereTheDeserializerDoes(string toml, int maxDepth)
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = maxDepth };

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml, options));
        var diagnostic = Assert.Single(SyntaxParser.Parse(toml, options).Diagnostics);

        Assert.Equal(exception.Span!.Value.Start.Line, diagnostic.Span.Start.Line);
        Assert.Equal(exception.Span!.Value.Start.Column, diagnostic.Span.Start.Column);
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

    // The depth MaxDepth limits: the root table is one level, and an array of tables and its elements are one level each
    private static int GetModelDepth(object value) => value switch
    {
        TomlTable table => 1 + table.Select(pair => GetModelDepth(pair.Value)).DefaultIfEmpty(0).Max(),
        TomlTableArray tableArray => 1 + tableArray.Select(GetModelDepth).DefaultIfEmpty(0).Max(),
        TomlArray array => 1 + array.Select(item => GetModelDepth(item!)).DefaultIfEmpty(0).Max(),
        _ => 0,
    };

    [Fact]
    public void Deserialize_HeadersReopeningArraysOfTables_RespectMaxDepth()
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 1; i <= 30; i++)
        {
            builder.Append("[[").Append(string.Join('.', Enumerable.Repeat("a", i))).Append("]]\n[x").Append(i).Append("]\n");
        }

        var toml = builder.ToString();

        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml, TomlSerializerOptions.Default with { MaxDepth = 32 }));
        Assert.True(SyntaxParser.Parse(toml, TomlSerializerOptions.Default with { MaxDepth = 32 }).HasErrors);
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntaxNode_Descendants_KeepsTheOrderOfARecursiveWalk(bool includeTokens)
    {
        var doc = SyntaxParser.Parse("# c\na = 1 # t\n[t] # h\nb = [1, {c = 2}] \n[[u]]\nd.e = 'x'\n");

        Assert.Equal(RecursiveDescendants(doc, includeTokens), doc.Descendants(includeTokens));
        Assert.Equal(RecursiveDescendants(doc.Tables.GetChild(0)!, includeTokens), doc.Tables.GetChild(0)!.Descendants(includeTokens));
        Assert.Equal(RecursiveDescendants(doc.Tables, includeTokens), doc.Tables.Descendants(includeTokens));

        static List<SyntaxNodeBase> RecursiveDescendants(SyntaxNode node, bool include)
        {
            var result = new List<SyntaxNodeBase>();
            Visit(node, isChildList: false);
            return result;

            void Visit(SyntaxNode current, bool isChildList)
            {
                if (!include && current is SyntaxToken)
                {
                    return;
                }

                if (!isChildList && include && current.LeadingTrivia is not null)
                {
                    result.AddRange(current.LeadingTrivia);
                }

                for (var i = 0; i < current.ChildrenCount; i++)
                {
                    if (current.GetChild(i) is { } child)
                    {
                        Visit(child, child is SyntaxList);
                    }
                }

                if (isChildList)
                {
                    return;
                }

                result.Add(current);
                if (include && current.TrailingTrivia is not null)
                {
                    result.AddRange(current.TrailingTrivia);
                }
            }
        }
    }

    [Fact]
    public void SyntaxNode_DescendantsAndTokens_OfADeepTree_DoNotOverflowTheStack()
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = int.MaxValue };
        var descendants = 0;
        var tokens = 0;
        DocumentSyntax? doc = null;

        // The parser needs a large stack for such a tree, the enumeration does not
        var parser = new Thread(() => doc = SyntaxParser.Parse(CreateNestedArrayToml(30_000), options), maxStackSize: 256 * 1024 * 1024);
        parser.Start();
        parser.Join();
        Assert.False(doc!.HasErrors, doc.Diagnostics.ToString());

        Assert.Null(RunWithSmallStack(() =>
        {
            descendants = doc.Descendants().Count();
            tokens = doc.Tokens().Count();
        }));

        Assert.True(descendants > 30_000, $"{descendants} descendants");
        Assert.True(tokens > 60_000, $"{tokens} tokens");
    }

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
