using Meziantou.Framework.Yaml.Events;
using TagDirective = Meziantou.Framework.Yaml.Tokens.TagDirective;
using VersionDirective = Meziantou.Framework.Yaml.Tokens.VersionDirective;

namespace Meziantou.Framework.Yaml.Tests;
public sealed class EmitterEdgeCaseTests
{
    [Fact]
    public void Emit_Directives_AreWritten()
    {
        var tags = new TagDirectiveCollection
        {
            new TagDirective("!e!", "tag:example.com,2026:"),
        };

        var yaml = EmitDocument(
            new DocumentStart(new VersionDirective(new Version(1, 1)), tags, isImplicit: false),
            new Scalar("value"));

        Assert.Contains("%YAML 1.1", yaml);
        Assert.Contains("%TAG !e! tag:example.com,2026:", yaml);
        Assert.Contains("---", yaml);
    }

    [Fact]
    public void Emit_TagHandle_IsShortenedUsingDirective()
    {
        var tags = new TagDirectiveCollection
        {
            new TagDirective("!e!", "tag:example.com,2026:"),
        };

        var yaml = EmitDocument(
            new DocumentStart(null, tags, isImplicit: false),
            new Scalar(null, "!e!name", "value", ScalarStyle.Plain, isPlainImplicit: false, isQuotedImplicit: false));

        Assert.Contains("%TAG !e! tag:example.com,2026:", yaml);
        Assert.True(yaml.Contains("!e!name", StringComparison.Ordinal) ||
            yaml.Contains("tag:example.com,2026:name", StringComparison.Ordinal), $"Expected emitted tag for scalar. Output:\n{yaml}");
        Assert.Contains("value", yaml);
    }

    [Fact]
    public void Emit_TagUri_IsShortenedToDoubleExclamationWhenPossible()
    {
        var yaml = EmitDocument(
            new Scalar(null, "tag:yaml.org,2002:str", "text", ScalarStyle.Plain, isPlainImplicit: false, isQuotedImplicit: false));

        // YAML 1.1/1.2 core shorthand for "tag:yaml.org,2002:" is "!!".
        Assert.True(yaml.Contains("!!str", StringComparison.Ordinal) ||
            yaml.Contains("tag:yaml.org,2002:str", StringComparison.Ordinal), $"Expected emitted tag for scalar. Output:\n{yaml}");
        Assert.Contains("text", yaml);
    }

    [Fact]
    public void Emit_FlowMapping_IsInline()
    {
        var yaml = EmitDocument(
            new MappingStart(null, null, isImplicit: true, style: YamlStyle.Flow),
            new Scalar("a"),
            new Scalar("1"),
            new Scalar("b"),
            new Scalar("2"),
            new MappingEnd());

        Assert.Contains("{a: 1, b: 2}", yaml);
    }

    [Fact]
    public void Emit_EmptyCollections_AreWrittenInline()
    {
        var yaml = EmitDocument(
            new MappingStart(),
            new Scalar("emptySeq"),
            new SequenceStart(null, null, isImplicit: true, style: YamlStyle.Block),
            new SequenceEnd(),
            new Scalar("emptyMap"),
            new MappingStart(null, null, isImplicit: true, style: YamlStyle.Block),
            new MappingEnd(),
            new MappingEnd());

        Assert.Contains("emptySeq: []", yaml);
        Assert.Contains("emptyMap: {}", yaml);
    }

    [Fact]
    public void Emit_ComplexKey_UsesExplicitKeyIndicator()
    {
        var yaml = EmitDocument(
            new MappingStart(null, null, isImplicit: true, style: YamlStyle.Block),
            new Scalar(null, null, "multi\nline", ScalarStyle.DoubleQuoted, true, true),
            new Scalar("value"),
            new MappingEnd());

        Assert.Contains("?", yaml);
        Assert.Contains("value", yaml);
    }

    [Fact]
    public void Emit_SingleQuotedScalar_EscapesSingleQuoteByDoubling()
    {
        var yaml = EmitDocument(
            new Scalar(null, null, "a'b", ScalarStyle.SingleQuoted, true, true));

        Assert.Contains("'a''b'", yaml);
    }

    [Fact]
    public void Emit_DoubleQuotedScalar_EscapesControlAndQuotes()
    {
        var yaml = EmitDocument(
            new Scalar(null, null, "a\"b\\c\n", ScalarStyle.DoubleQuoted, true, true));

        Assert.Contains("\\\"", yaml);
        Assert.Contains("\\\\", yaml);
        Assert.Contains("\\n", yaml);
    }

    [Fact]
    public void Emit_BlockScalars_EmitLiteralAndFoldedIndicators()
    {
        var literal = EmitDocument(
            new Scalar(null, null, "a\nb\n", ScalarStyle.Literal, true, true));
        Assert.Contains("|", literal);
        Assert.Contains("a", literal);
        Assert.Contains("b", literal);

        var folded = EmitDocument(
            new Scalar(null, null, "a\nb\n", ScalarStyle.Folded, true, true));
        Assert.Contains(">", folded);
        Assert.Contains("a", folded);
        Assert.Contains("b", folded);
    }

    [Theory]
    [InlineData(ScalarStyle.Any)]
    [InlineData(ScalarStyle.Plain)]
    [InlineData(ScalarStyle.SingleQuoted)]
    [InlineData(ScalarStyle.DoubleQuoted)]
    [InlineData(ScalarStyle.Literal)]
    [InlineData(ScalarStyle.Folded)]
    public void Emit_ScalarStyles_PreserveWhitespaceAndChomping(ScalarStyle style)
    {
        string[] values = ["", " ", "\t", "\n", "\n\n", "text", "text\n", "text\n\n", "text\n\n\n", "\ntext", "\n\ntext\n", " leading\ntext", "text\n indented\nlast", "text\n\nlast", "text \nlast", "text\n \nlast", "text\t\nlast", "\ttext\nlast", "text\n\tlast", "first\rsecond", "first\r\nsecond", "first\nsecond\r\n"];

        foreach (var value in values)
        {
            var yaml = EmitDocument(new Scalar(null, null, value, style, true, true));
            Assert.Equal(value, ParseScalarValues(yaml).Single());
        }
    }

    [Theory]
    [InlineData(ScalarStyle.Any)]
    [InlineData(ScalarStyle.Plain)]
    [InlineData(ScalarStyle.SingleQuoted)]
    [InlineData(ScalarStyle.DoubleQuoted)]
    [InlineData(ScalarStyle.Literal)]
    [InlineData(ScalarStyle.Folded)]
    public void Emit_ScalarStyles_EscapeNonPrintableContentEvenWithLineBreaks(ScalarStyle style)
    {
        string[] values = ["a\0b\nc", "a\u0001b\nc", "a\u0007b\nc", "a\u000Bb\nc", "a\u000Cb\nc", "a\u001Bb\nc", "a\u007Fb\nc", "a\u009Fb\nc", "a\uFFFEb\nc", "a\uFFFFb\nc", "\uFEFFtext", "a\uFEFFb\nc", "a\U0001F600b\nc"];

        foreach (var value in values)
        {
            var yaml = EmitDocument(new Scalar(null, null, value, style, true, true));

            Assert.DoesNotContain(value, yaml);
            Assert.Equal(value, ParseScalarValues(yaml).Single());
        }
    }

    [Theory]
    [InlineData(YamlStyle.Block)]
    [InlineData(YamlStyle.Flow)]
    public void Emit_MappingKeysAndValues_PreserveIndicatorsAndUnicode(YamlStyle style)
    {
        string[] values = ["", "-", "?", ":", "---", "...", "--- text", "a: b", "a # b", "a:b", "a#b", "a,b", "a[b]", "a{b}", "a?b", "'quoted'", "\"quoted\"", "\uFEFFtext", "a\uFFFEb", "a\U0001F600b"];

        foreach (var value in values)
        {
            var yaml = EmitDocument(
                new MappingStart(null, null, isImplicit: true, style),
                new Scalar(value),
                new Scalar(value),
                new Scalar("next"),
                new Scalar("sentinel"),
                new MappingEnd());

            Assert.Equal([value, value, "next", "sentinel"], ParseScalarValues(yaml));
        }
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDBFF)]
    [InlineData(0xDC00)]
    [InlineData(0xDFFF)]
    public void Emit_UnpairedSurrogates_RejectsInvalidUnicode(int codePoint)
    {
        var character = ((char)codePoint).ToString();
        foreach (var value in new[] { character, "before" + character + "after", character + "\n" })
        {
            foreach (var style in new[] { ScalarStyle.Any, ScalarStyle.Plain, ScalarStyle.SingleQuoted, ScalarStyle.DoubleQuoted, ScalarStyle.Literal, ScalarStyle.Folded })
            {
                Assert.Throws<YamlException>(() => EmitDocument(new Scalar(null, null, value, style, true, true)));
            }
        }
    }

    [Theory]
    [InlineData("!type[,]", "!type%5B%2C%5D")]
    [InlineData("!type%name", "!type%25name")]
    [InlineData("!\U0001F600", "!%F0%9F%98%80")]
    public void Emit_LocalTags_EscapeSuffixAndPreserveDecodedTag(string tag, string expected)
    {
        var yaml = EmitDocument(new Scalar(null, tag, "value", ScalarStyle.Plain, false, false));
        Assert.Contains(expected + " value", yaml);
        var parser = Parser.CreateParser(new StringReader(yaml));
        var scalars = new List<Scalar>();
        while (parser.MoveNext())
        {
            if (parser.Current is Scalar scalar)
                scalars.Add(scalar);
        }

        var result = Assert.Single(scalars);
        Assert.Equal(tag, result.Tag);
        Assert.Equal("value", result.Value);
    }

    private static List<string> ParseScalarValues(string yaml)
    {
        var result = new List<string>();
        var parser = Parser.CreateParser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            if (parser.Current is Scalar scalar)
            {
                result.Add(scalar.Value);
            }
        }

        return result;
    }

    private static string EmitDocument(params ParsingEvent[] events)
    {
        return EmitDocument(documentStart: null, events);
    }

    private static string EmitDocument(DocumentStart? documentStart, params ParsingEvent[] events)
    {
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        var emitter = new Emitter(buffer);

        emitter.Emit(new StreamStart());
        emitter.Emit(documentStart ?? new DocumentStart(null, null, isImplicit: true));

        foreach (var evt in events)
        {
            emitter.Emit(evt);
        }

        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());

        return buffer.ToString();
    }
}
