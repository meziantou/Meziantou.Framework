namespace Meziantou.Framework.Yaml.Tests;

public sealed class YamlReadOnlyDocumentTests
{
    [Fact]
    public void Parse_ReadsMappingsSequencesAndScalars()
    {
        using var document = YamlReadOnlyDocument.Parse("""
            name: demo # comment
            replicas: 3
            enabled: true
            ratio: .inf
            empty:
            tags: [a, "b c"]
            nested:
              key: |
                line 1
                line 2
            """);

        var root = document.Root;
        Assert.Equal(YamlReadOnlyNodeKind.Mapping, root.Kind);
        Assert.Equal(7, root.GetLength());
        Assert.Equal("demo", root["name"].GetString());
        Assert.True(root["replicas"].TryGetInt64(out var replicas));
        Assert.Equal(3, replicas);
        Assert.True(root["enabled"].TryGetBoolean(out var enabled));
        Assert.True(enabled);
        Assert.True(root["ratio"].TryGetDouble(out var ratio));
        Assert.Equal(double.PositiveInfinity, ratio);
        Assert.True(root["empty"].IsNull);
        Assert.Equal(["a", "b c"], root["tags"].EnumerateSequence().Select(node => node.GetString()));
        Assert.True(root["tags"].IsFlow);
        Assert.Equal("b c", root["tags"][1].GetString());
        Assert.Equal(ScalarStyle.DoubleQuoted, root["tags"][1].ScalarStyle);
        Assert.Equal("line 1\nline 2\n", root["nested"]["key"].GetString());
        Assert.True(root.TryGetProperty("replicas"u8, out var node));
        Assert.True(node.ValueEquals("3"u8));
        Assert.False(root.TryGetProperty("missing"u8, out _));
    }

    [Fact]
    public void ReadmeExample_Read()
    {
        #region YamlReadOnlyDocument_Read
        using var document = YamlReadOnlyDocument.Parse("""
            services:
              web:
                image: nginx
                replicas: 3
                ports: [80, 443]
            """);

        var web = document.Root["services"]["web"];
        Assert.Equal("nginx", web["image"].GetString());
        Assert.True(web["replicas"].TryGetInt64(out var replicas));
        Assert.Equal(3, replicas);

        var ports = new List<long>();
        foreach (var port in web["ports"].EnumerateSequence())
        {
            Assert.True(port.TryGetInt64(out var value));
            ports.Add(value);
        }

        Assert.Equal([80L, 443L], ports);

        // A key can be matched against UTF-8 bytes without allocating a string
        Assert.True(web.TryGetProperty("image"u8, out var image));
        Assert.Equal("nginx", image.GetRawText());
        #endregion
    }

    [Fact]
    public void Node_GetRawText_ReturnsTheSourceBytes()
    {
        var yaml = "a: &x !!str value\nb:\n  - 1\n  - [2, 3] # c\nc: *x\n";
        using var document = YamlReadOnlyDocument.Parse(yaml);
        var root = document.Root;
        Assert.Equal("&x !!str value", root["a"].GetRawText());
        Assert.Equal("- 1\n  - [2, 3]", root["b"].GetRawText());
        Assert.Equal("x", root["a"].Anchor);
        Assert.Equal("tag:yaml.org,2002:str", root["a"].Tag);
        Assert.Equal(YamlReadOnlyNodeKind.Alias, root["c"].Kind);
        Assert.Equal("value", root["c"].ResolveAlias().GetString());
    }

    [Fact]
    public void ResolveAlias_DoesNotFindAnAnchorOfAnotherDocument()
    {
        using var document = YamlReadOnlyDocument.Parse("&a first\n---\n*a\n");

        Assert.Throws<InvalidOperationException>(() => document.GetDocumentRoot(1).ResolveAlias());
    }

    [Fact]
    public void Parse_StringWithALoneSurrogate_Throws()
    {
        var exception = Assert.Throws<SyntaxErrorException>(() => YamlReadOnlyDocument.Parse("a: b\nc: x\uD800y\n"));

        Assert.Equal((1, 4), (exception.Start.Line, exception.Start.Column));
    }

    [Theory]
    [InlineData(".inf")]
    [InlineData("+.inf")]
    [InlineData(".Inf")]
    [InlineData("+.Inf")]
    [InlineData(".INF")]
    [InlineData("+.INF")]
    public void TryGetDouble_PositiveInfinity(string text)
    {
        using var document = YamlReadOnlyDocument.Parse(text);

        Assert.True(document.Root.TryGetDouble(out var value));
        Assert.Equal(double.PositiveInfinity, value);
    }

    [Fact]
    public void Node_Positions()
    {
        #region YamlReadOnlyDocument_Positions
        var yaml = "name: café\r\nemoji: 😀 # comment\r\nlist: [a, b]\r\n";
        using var document = YamlReadOnlyDocument.Parse(yaml);
        var list = document.Root["list"];

        // Start and End are UTF-16 positions: they slice the string the document was parsed from
        Assert.Equal("[a, b]", yaml[list.Start.Index..list.End.Index]);
        Assert.Equal(2, list.Start.Line);
        Assert.Equal(6, list.Start.Column);

        // Utf8Offset and Utf8Length are positions in the UTF-8 bytes: "é" and "😀" are more bytes than characters
        Assert.Equal(list.Start.Index + 3, list.Utf8Offset);
        Assert.Equal("[a, b]"u8, list.GetRawUtf8Text());
        #endregion
    }

    [Theory]
    [InlineData("a: x\nb: [é, 😀]\n")]
    [InlineData("\uFEFFa: x\r\nb: [é, 😀]\r")]
    [InlineData("a: x\rb: [é, 😀]")]
    [InlineData("# é\n---\nb: [é, 😀]\n")]
    public void Node_Positions_MatchTheParser(string yaml)
    {
        var expected = new List<string>();
        var parser = Parser.CreateParser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            if (parser.Current is Events.Scalar or Events.SequenceStart or Events.MappingStart)
            {
                expected.Add($"{parser.Current.Start.Index}:{parser.Current.Start.Line}:{parser.Current.Start.Column}");
            }
        }

        using var document = YamlReadOnlyDocument.Parse(yaml);
        var actual = new List<string>();
        AddStarts(document.Root, actual);
        Assert.Equal(expected, actual);

        static void AddStarts(YamlReadOnlyNode node, List<string> starts)
        {
            starts.Add($"{node.Start.Index}:{node.Start.Line}:{node.Start.Column}");
            switch (node.Kind)
            {
                case YamlReadOnlyNodeKind.Mapping:
                    foreach (var entry in node.EnumerateMapping())
                    {
                        AddStarts(entry.Key, starts);
                        AddStarts(entry.Value, starts);
                    }

                    break;

                case YamlReadOnlyNodeKind.Sequence:
                    foreach (var item in node.EnumerateSequence())
                    {
                        AddStarts(item, starts);
                    }

                    break;
            }
        }
    }

    [Fact]
    public void Node_ValueEquals_ComparesWithUtf16Text()
    {
        using var document = YamlReadOnlyDocument.Parse("café: \"a\\tb\"\nkey: 😀\n");
        var root = document.Root;
        Assert.True(root.TryGetProperty("café".AsSpan(), out var value));
        Assert.True(value.ValueEquals("a\tb".AsSpan()));
        Assert.True(value.ValueEquals("a\tb"));
        Assert.False(value.ValueEquals("a\tbc"));
        Assert.True(root["key"].ValueEquals("😀"));
        Assert.False(root["key"].ValueEquals("\uD83D"));
        Assert.False(root.ValueEquals("key"));
    }

    [Fact]
    public void Parse_MultipleDocuments()
    {
        using var document = YamlReadOnlyDocument.Parse("a\n---\nb\n...\n--- [c]\n");
        Assert.Equal(3, document.DocumentCount);
        Assert.Equal("a", document.GetDocumentRoot(0).GetString());
        Assert.Equal("b", document.GetDocumentRoot(1).GetString());
        Assert.Equal("c", document.GetDocumentRoot(2)[0].GetString());
    }

    [Fact]
    public void Dispose_ThenAccess_Throws()
    {
        var document = YamlReadOnlyDocument.Parse("a: b");
        var root = document.Root;
        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => root.GetLength());
    }

    [Theory]
    [MemberData(nameof(Utf8YamlReaderTests.ValidYamlTestSuiteCases), MemberType = typeof(Utf8YamlReaderTests))]
    public void YamlTestSuite_ValidCase_MatchesTheReader(string id)
    {
        var utf8 = Encoding.UTF8.GetBytes(YamlTestSuite.Get(id).Yaml);
        var expected = Utf8YamlReaderTests.ReadEvents(utf8).Split('\n').Where(line => !line.StartsWith("+STR", StringComparison.Ordinal) && !line.StartsWith("-STR", StringComparison.Ordinal) && !line.StartsWith("+DOC", StringComparison.Ordinal) && !line.StartsWith("-DOC", StringComparison.Ordinal));
        using var document = YamlReadOnlyDocument.Parse(utf8);
        var actual = new List<string>();
        for (var i = 0; i < document.DocumentCount; i++)
        {
            AddEvents(document.GetDocumentRoot(i), actual);
        }

        Assert.Equal(expected, actual);
    }

    // With normalizeScalarStyles, a scalar is only distinguished by the type its style gives it: an untagged plain scalar
    // may be a null, a boolean or a number, while another scalar is a string, like a plain one that is not a null, a
    // boolean or a number
    private static bool IsTypedPlain(YamlReadOnlyNode node)
        => node.ScalarStyle is ScalarStyle.Plain && node.Tag is null && Meziantou.Framework.Yaml.Serialization.YamlScalar.ResolvesToNonString(node.GetString(), YamlSchemaKind.Core);

    internal static void AddEvents(YamlReadOnlyNode node, List<string> events, bool normalizeScalarStyles = false)
    {
        var properties = (node.Kind is YamlReadOnlyNodeKind.Alias || node.Anchor is null ? "" : " &" + node.Anchor) + (node.Tag is null ? "" : " <" + node.Tag + ">");
        switch (node.Kind)
        {
            case YamlReadOnlyNodeKind.Alias:
                events.Add("=ALI *" + node.Anchor);
                break;

            case YamlReadOnlyNodeKind.Scalar:
                events.Add("=VAL" + properties + " " + (normalizeScalarStyles ? (IsTypedPlain(node) ? ':' : '"') : YamlTestSuite.ScalarIndicator(node.ScalarStyle)) + YamlTestSuite.EscapeEventValue(node.GetString()));
                break;

            case YamlReadOnlyNodeKind.Sequence:
                events.Add("+SEQ" + (node.IsFlow ? " []" : "") + properties);
                foreach (var item in node.EnumerateSequence())
                {
                    AddEvents(item, events, normalizeScalarStyles);
                }

                events.Add("-SEQ");
                break;

            default:
                events.Add("+MAP" + (node.IsFlow ? " {}" : "") + properties);
                foreach (var entry in node.EnumerateMapping())
                {
                    AddEvents(entry.Key, events, normalizeScalarStyles);
                    AddEvents(entry.Value, events, normalizeScalarStyles);
                }

                events.Add("-MAP");
                break;
        }
    }
}
