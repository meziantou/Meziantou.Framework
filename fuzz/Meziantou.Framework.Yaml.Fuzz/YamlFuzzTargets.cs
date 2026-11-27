using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Nodes;
using Meziantou.Framework.Yaml.Syntax;
using TagDirective = Meziantou.Framework.Yaml.Tokens.TagDirective;

namespace Meziantou.Framework.Yaml.Fuzz;

/// <summary>
/// Malformed YAML must only throw <see cref="YamlException"/>. The two look-ahead buffers of the parser must produce the
/// same events, the syntax tree must accept exactly what the parser accepts, a loaded stream must save to YAML that
/// loads back to the same text, the emitter must write events that parse back to the same nodes, and the overloads of
/// the serializer must agree and round-trip the values they read.
/// </summary>
public static class YamlFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "---", "...", "%YAML 1.2", "%YAML 1.1", "%TAG ! tag:example.com,2000:", "%TAG !e! tag:e,2000:", "!", "!!", "!!str", "!!int", "!!float", "!!bool", "!!null", "!!map", "!!seq", "!!set", "!!omap", "!!binary",
        "!<tag:yaml.org,2002:str>", "!e!local", "&a ", "*a", "<<", "? ", ": ", "- ", "-", ":", "?", "|", ">", "|-", "|+", ">-", ">+", "|2", ">1-", "{", "}", "[", "]", ", ", "#", " #", "'", "''", "\"", "\\", "\\u263A", "\\x0d", "\\N", "\\_", "\\L", "\\P", "\\\n",
        "\t", "\n", "\r\n", "\r", "\u0085", "\u2028", "\uFEFF", "  ", "null", "~", "true", "false", "yes", ".inf", "-.inf", ".nan", "0x1F", "0o17", "1e3", "2001-12-14t21:59:43.10-05:00",
    ];

    private static readonly TagDirective[] DefaultTagDirectives = [new("!", "!"), new("!!", "tag:yaml.org,2002:")];

    private static readonly YamlSerializerOptions LoadOptions =new() { MaxDepth = 64, MaxAliasExpansionNodeCount = 10_000 };

    private static readonly string[] NumericsTokens =
    [
        "Real", "Imaginary", "real", "imaginary", "Magnitude", "Phase", "Complex", "Optional", "Values", "Ratio", "BFloat16",
        "Decimal32", "Decimal64", "Decimal128", "ComplexDouble", "ComplexHalf", "ComplexDecimal", ".nan", ".inf", "-.inf",
        "1.50", "1E+400", "1e-400", "1_000.5", "9.999999E+96", "0.1000000000000000000000000000000001", "{Real: 1.5, Imaginary: -2.0}",
    ];

    private static readonly YamlSerializerOptions[] NumericsOptions =
    [
        new() { MaxDepth = 64 },
        new() { MaxDepth = 64, PropertyNamingPolicy = YamlNamingPolicy.CamelCase },
        new() { MaxDepth = 64, PropertyNameCaseInsensitive = true },
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("yaml-parser", Parser) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-syntax-tree", SyntaxTree) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-model", Model) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-deserialize", Deserialize) { Seeds = GetDeserializeSeeds, Dictionary = Tokens },
        new("yaml-emitter", EmitterRoundTrip) { Seeds = GetEmitterSeeds, Dictionary = Tokens },
        new("yaml-numerics", Numerics) { Seeds = GetNumericsSeeds, Dictionary = [.. Tokens, .. NumericsTokens] },
        new("yaml-utf8-reader", Utf8Reader) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-model-preserving-formatting", ModelPreservingFormatting) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-utf8-writer", Utf8Writer) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// <see cref="Utf8YamlReader"/> reads the same nodes as the scanner-based parser (<see cref="Parser{TBuffer}"/>) from
    /// every document that parser accepts. Its tokens, indicators and properties cover every byte that is not
    /// whitespace, a line break or a comment. Reading the input in parts gives the same tokens at the same positions,
    /// and <see cref="YamlDocument"/> holds the same nodes. A clone of a node holds the same nodes at the same positions.
    /// </summary>
    private static void Utf8Reader(ReadOnlySpan<byte> data)
    {
        var yaml = FuzzText.Decode(data);
        var utf8 = Encoding.UTF8.GetBytes(yaml);
        var actual = ReadUtf8ReaderNodes(utf8);
        if (actual.Error is null)
        {
            var coverageError = CheckUtf8ReaderTokenCoverage(utf8);
            FuzzAssert.True(coverageError is null, $"The tokens of the reader do not cover the input: {coverageError}");

            var positions = ReadUtf8ReaderPositions(utf8, chunkSize: 0);
            var chunkSize = 1 + (data.Length % 13);
            FuzzAssert.Equal(positions, ReadUtf8ReaderPositions(utf8, chunkSize), $"Reading the input in parts of {chunkSize} bytes gives different tokens");

            using var document = YamlDocument.Parse(utf8);
            var documentNodes = new List<string>();
            for (var i = 0; i < document.DocumentCount; i++)
            {
                AddReadOnlyNodes(document.GetDocumentRoot(i), documentNodes);
            }

            var readerNodes = actual.Events.Split('\n').Where(line => line.Length > 0 && !line.StartsWith("+DOC", StringComparison.Ordinal) && !line.StartsWith("-DOC", StringComparison.Ordinal));
            FuzzAssert.Equal(string.Join('\n', readerNodes), string.Join('\n', documentNodes), "The read-only document and the reader read different nodes");
            CheckClones(document, data.Length);
        }

        // A byte order mark is a document prefix, which can repeat (YAML 1.2 §9.2): the parser reads an empty document for
        // a second one at the start of the stream, which the grammar does not have
        var expected = ReadParserNodes(yaml);
        if (expected.Error is null && !yaml.StartsWith("\uFEFF\uFEFF", StringComparison.Ordinal))
        {
            FuzzAssert.True(actual.Error is null, $"The reader rejects YAML that the parser accepts: {actual.Error}");
            FuzzAssert.Equal(expected.Events, actual.Events, "The reader and the parser read different nodes");
        }
    }

    /// <summary>
    /// The writer writes YAML that reads back as the same nodes: the read-only document of the input, written with its
    /// styles, anchors and tags, gives the same values with the same types, and the scanner-based parser reads the
    /// output the same way. Every scalar written as a string with the default style reads back as that string.
    /// </summary>
    private static void Utf8Writer(ReadOnlySpan<byte> data)
    {
        var utf8 = Encoding.UTF8.GetBytes(FuzzText.Decode(data));
        YamlDocument source;
        try
        {
            source = YamlDocument.Parse(utf8);
        }
        catch (YamlException)
        {
            return;
        }

        using (source)
        {
            var output = WriteYaml(source.WriteTo);
            using (var written = ParseWriterOutput(output))
            {
                FuzzAssert.Equal(ReadTypedNodes(source), ReadTypedNodes(written), "The written YAML reads as other nodes");
            }

            // NEL, LS and PS are line breaks for the parser, like in YAML 1.1: the writer only writes them in the names of
            // anchors, like the input
            var text = Encoding.UTF8.GetString(output);
            if (text.AsSpan().IndexOfAny('\u0085', '\u2028', '\u2029') < 0)
            {
                var parserNodes = ReadParserNodes(text);
                FuzzAssert.True(parserNodes.Error is null, $"The parser rejects the written YAML: {parserNodes.Error}\n{text}");
                FuzzAssert.Equal(parserNodes.Events, ReadUtf8ReaderNodes(output).Events, "The parser and the reader read the written YAML differently");
            }

            // Every scalar as a string, with the style the writer chooses, and comments wherever they can be written
            var strings = WriteYaml(writer =>
            {
                var position = 0;
                for (var i = 0; i < source.DocumentCount; i++)
                {
                    if (i > 0)
                    {
                        writer.WriteStartDocument();
                    }

                    writer.WriteComment(CommentText);
                    WriteAsStrings(source.GetDocumentRoot(i), writer, inFlow: false, ref position);
                    writer.WriteComment(CommentText);
                }
            });

            using var stringDocument = ParseWriterOutput(strings);
            FuzzAssert.Equal(source.DocumentCount, stringDocument.DocumentCount, "The written YAML has another number of documents");
            for (var i = 0; i < source.DocumentCount; i++)
            {
                AssertSameStrings(source.GetDocumentRoot(i), stringDocument.GetDocumentRoot(i));
            }
        }

        static byte[] WriteYaml(Action<Utf8YamlWriter> write)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8YamlWriter(buffer))
            {
                write(writer);
            }

            return buffer.WrittenSpan.ToArray();
        }

        static YamlDocument ParseWriterOutput(byte[] output)
        {
            try
            {
                return YamlDocument.Parse(output);
            }
            catch (YamlException ex)
            {
                throw new FuzzAssertionException($"The written YAML is invalid: {ex.Message}\n{Encoding.UTF8.GetString(output)}", ex);
            }
        }
    }

    // Indicators, a document marker and a line break: a comment must not change what is around it
    private const string CommentText = "a: [b] {c} 'd' \"e\" #f &g *h !i\n--- ...";

    private static void WriteAsStrings(YamlElement node, Utf8YamlWriter writer, bool inFlow, ref int position)
    {
        if (node.Kind is not YamlElementKind.Alias && node.Anchor is { } anchor)
        {
            writer.WriteAnchor(anchor);
        }

        switch (node.Kind)
        {
            case YamlElementKind.Alias:
                writer.WriteAliasValue(node.Anchor);
                break;

            case YamlElementKind.Scalar:
                writer.WriteStringValue(node.GetString());
                break;

            case YamlElementKind.Sequence:
                var flowSequence = inFlow || node.IsFlow;
                writer.WriteStartSequence(node.IsFlow ? YamlStyle.Flow : YamlStyle.Block);
                foreach (var item in node.EnumerateSequence())
                {
                    WriteCommentAt(writer, flowSequence, ref position);
                    WriteAsStrings(item, writer, flowSequence, ref position);
                }

                WriteCommentAt(writer, flowSequence, ref position);
                writer.WriteEndSequence();
                break;

            default:
                var flowMapping = inFlow || node.IsFlow;
                writer.WriteStartMapping(node.IsFlow ? YamlStyle.Flow : YamlStyle.Block);
                foreach (var entry in node.EnumerateMapping())
                {
                    WriteCommentAt(writer, flowMapping, ref position);
                    WriteAsStrings(entry.Key, writer, flowMapping, ref position);
                    WriteAsStrings(entry.Value, writer, flowMapping, ref position);
                }

                WriteCommentAt(writer, flowMapping, ref position);
                writer.WriteEndMapping();
                break;
        }
    }

    // A comment before every other entry of the block collections, and at the end of some of them
    private static void WriteCommentAt(Utf8YamlWriter writer, bool inFlow, ref int position)
    {
        if (!inFlow && position++ % 2 == 0)
        {
            writer.WriteComment(CommentText);
        }
    }

    private static void AssertSameStrings(YamlElement expected, YamlElement actual)
    {
        FuzzAssert.Equal(expected.Kind, actual.Kind, "A node written as a string has another kind");
        switch (expected.Kind)
        {
            case YamlElementKind.Scalar:
                var value = expected.GetString();
                FuzzAssert.Equal(EscapeValue(value), EscapeValue(actual.GetString()), "A string reads back as another value");
                FuzzAssert.True(actual.ScalarStyle is not ScalarStyle.Plain || !ResolvesToNonString(value), $"The string '{EscapeValue(value)}' is written plain, which reads as another type");
                break;

            case YamlElementKind.Sequence:
                FuzzAssert.Equal(expected.GetLength(), actual.GetLength(), "A sequence has another number of items");
                var expectedItems = expected.EnumerateSequence();
                var actualItems = actual.EnumerateSequence();
                while (expectedItems.MoveNext() && actualItems.MoveNext())
                {
                    AssertSameStrings(expectedItems.Current, actualItems.Current);
                }

                break;

            case YamlElementKind.Mapping:
                FuzzAssert.Equal(expected.GetLength(), actual.GetLength(), "A mapping has another number of entries");
                var expectedEntries = expected.EnumerateMapping();
                var actualEntries = actual.EnumerateMapping();
                while (expectedEntries.MoveNext() && actualEntries.MoveNext())
                {
                    AssertSameStrings(expectedEntries.Current.Key, actualEntries.Current.Key);
                    AssertSameStrings(expectedEntries.Current.Value, actualEntries.Current.Value);
                }

                break;
        }
    }

    // A plain scalar that the core schema reads as a null, a boolean or a number, or a YAML 1.1 boolean
    private static bool ResolvesToNonString(string value)
        => Yaml.Serialization.YamlScalar.IsSchemaKeyword(value) || Yaml.Serialization.YamlScalar.ResolvesToNonString(value, YamlSchemaKind.Core);

    // The nodes, with only the part of the scalar style that gives the type: an untagged plain scalar may be a null, a
    // boolean or a number, while any other scalar is a string
    private static string ReadTypedNodes(YamlDocument document)
    {
        var nodes = new List<string>();
        for (var i = 0; i < document.DocumentCount; i++)
        {
            AddTypedNodes(document.GetDocumentRoot(i), nodes);
        }

        return string.Join('\n', nodes);
    }

    private static void AddTypedNodes(YamlElement node, List<string> nodes)
    {
        var properties = (node.Kind is YamlElementKind.Alias || node.Anchor is null ? "" : " &" + node.Anchor) + (node.Tag is null ? "" : " !" + node.Tag);
        switch (node.Kind)
        {
            case YamlElementKind.Alias:
                nodes.Add("=ALI *" + node.Anchor);
                break;

            case YamlElementKind.Scalar:
                var value = node.GetString();
                var typed = node.ScalarStyle is ScalarStyle.Plain && node.Tag is null && Yaml.Serialization.YamlScalar.ResolvesToNonString(value, YamlSchemaKind.Core);
                nodes.Add("=VAL" + properties + (typed ? " typed " : " string ") + EscapeValue(value));
                break;

            case YamlElementKind.Sequence:
                nodes.Add("+SEQ" + (node.IsFlow ? " []" : "") + properties);
                foreach (var item in node.EnumerateSequence())
                {
                    AddTypedNodes(item, nodes);
                }

                nodes.Add("-SEQ");
                break;

            default:
                nodes.Add("+MAP" + (node.IsFlow ? " {}" : "") + properties);
                foreach (var entry in node.EnumerateMapping())
                {
                    AddTypedNodes(entry.Key, nodes);
                    AddTypedNodes(entry.Value, nodes);
                }

                nodes.Add("-MAP");
                break;
        }
    }

    // The positions of the tokens, read in one buffer (chunkSize 0) or in parts like a stream
    private static string ReadUtf8ReaderPositions(byte[] utf8, int chunkSize)
    {
        var tokens = new StringBuilder();
        var state = default(Utf8YamlReaderState);
        var start = 0;
        var available = chunkSize == 0 ? utf8.Length : Math.Min(chunkSize, utf8.Length);
        while (true)
        {
            var isFinalBlock = start + available == utf8.Length;
            var reader = new Utf8YamlReader(utf8.AsSpan(start, available), isFinalBlock, state);
            while (reader.Read())
            {
                tokens.Append(CultureInfo.InvariantCulture, $"{reader.TokenKind} {reader.BufferOffset + reader.TokenStart}+{reader.TokenLength} {(reader.TokenKind is Utf8YamlTokenKind.Scalar ? reader.GetString() : "")}\n");
            }

            if (isFinalBlock)
                return tokens.ToString();

            var consumed = reader.BytesConsumed;
            state = reader.CurrentState;
            start += consumed;
            available = Math.Min(available - consumed + chunkSize, utf8.Length - start);
        }
    }

    // A value on one line, to compare the nodes line by line
    private static string EscapeValue(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    // The data of a stream: the nodes, their tags and their values, without the styles the text can change
    private static string Represent(YamlStream stream)
    {
        var builder = new StringBuilder();
        foreach (var document in stream)
        {
            builder.Append("+DOC\n");
            if (document.Contents is not null)
            {
                Represent(document.Contents, builder);
            }
        }

        return builder.ToString();

        static void Represent(YamlNode node, StringBuilder builder)
        {
            switch (node)
            {
                case YamlMapping mapping:
                    builder.Append("+MAP ").Append(mapping.Tag).Append('\n');
                    foreach (var entry in mapping)
                    {
                        Represent(entry.Key, builder);
                        if (entry.Value is not null)
                        {
                            Represent(entry.Value, builder);
                        }
                    }

                    builder.Append("-MAP\n");
                    break;

                case YamlSequence sequence:
                    builder.Append("+SEQ ").Append(sequence.Tag).Append('\n');
                    foreach (var item in sequence)
                    {
                        Represent(item, builder);
                    }

                    builder.Append("-SEQ\n");
                    break;

                case YamlValue value:
                    // An untagged scalar has the tag the core schema resolves; "", "~" and "null" are the same null
                    var tag = Schemas.CoreSchema.Instance.ExpandTag(value.Tag is null or "!" ? Schemas.CoreSchema.Instance.GetDefaultTag(value.Scalar) : value.Tag);
                    var text = tag == Schemas.CoreSchema.Instance.ExpandTag("!!null") ? "" : value.Value;
                    builder.Append("=VAL ").Append(tag).Append(' ').Append(EscapeValue(text)).Append('\n');
                    break;
            }
        }
    }

    // A clone of a node holds the same nodes, with the same text and positions, and its aliases resolve to the same nodes.
    // Every node is cloned in a small document, and a sample of them in a larger one.
    private static void CheckClones(YamlDocument document, int seed)
    {
        var nodes = new List<YamlElement>();
        for (var i = 0; i < document.DocumentCount; i++)
        {
            CollectReadOnlyNodes(document.GetDocumentRoot(i), nodes);
        }

        var step = Math.Max(1, nodes.Count / 32);
        for (var i = seed % step; i < nodes.Count; i += step)
        {
            var node = nodes[i];
            var clone = node.Clone();
            var expected = new List<string>();
            var actual = new List<string>();
            DescribeReadOnlyNodes(node, expected);
            DescribeReadOnlyNodes(clone, actual);
            FuzzAssert.Equal(string.Join('\n', expected), string.Join('\n', actual), "The clone of a node holds other nodes");
            FuzzAssert.True(clone.Clone() == clone, "The clone of a clone is another node");
        }

        static void CollectReadOnlyNodes(YamlElement node, List<YamlElement> nodes)
        {
            nodes.Add(node);
            if (node.Kind is YamlElementKind.Sequence)
            {
                foreach (var item in node.EnumerateSequence())
                {
                    CollectReadOnlyNodes(item, nodes);
                }
            }
            else if (node.Kind is YamlElementKind.Mapping)
            {
                foreach (var entry in node.EnumerateMapping())
                {
                    CollectReadOnlyNodes(entry.Key, nodes);
                    CollectReadOnlyNodes(entry.Value, nodes);
                }
            }
        }

        static void DescribeReadOnlyNodes(YamlElement node, List<string> lines)
        {
            var descendants = new List<YamlElement>();
            CollectReadOnlyNodes(node, descendants);
            AddReadOnlyNodes(node, lines);
            foreach (var descendant in descendants)
            {
                var target = "";
                if (descendant.Kind is YamlElementKind.Alias)
                {
                    try
                    {
                        target = " -> " + descendant.ResolveAlias().Start;
                    }
                    catch (InvalidOperationException)
                    {
                        target = " -> undefined";
                    }
                }

                lines.Add(FormattableString.Invariant($"{descendant.Start} {descendant.End} {descendant.Utf8Offset}+{descendant.Utf8Length} {EscapeValue(descendant.GetRawText())}{target}"));
            }
        }
    }

    private static void AddReadOnlyNodes(YamlElement node, List<string> nodes)
    {
        var properties = (node.Kind is YamlElementKind.Alias || node.Anchor is null ? "" : " &" + node.Anchor) + (node.Tag is null ? "" : " !" + node.Tag);
        switch (node.Kind)
        {
            case YamlElementKind.Alias:
                nodes.Add("=ALI *" + node.Anchor);
                break;

            case YamlElementKind.Scalar:
                nodes.Add("=VAL" + properties + " " + node.ScalarStyle.ToString() + " " + EscapeValue(node.GetString()));
                break;

            case YamlElementKind.Sequence:
                nodes.Add("+SEQ" + (node.IsFlow ? " []" : "") + properties);
                foreach (var item in node.EnumerateSequence())
                {
                    AddReadOnlyNodes(item, nodes);
                }

                nodes.Add("-SEQ");
                break;

            default:
                nodes.Add("+MAP" + (node.IsFlow ? " {}" : "") + properties);
                foreach (var entry in node.EnumerateMapping())
                {
                    AddReadOnlyNodes(entry.Key, nodes);
                    AddReadOnlyNodes(entry.Value, nodes);
                }

                nodes.Add("-MAP");
                break;
        }
    }

    /// <summary>
    /// A loaded stream that did not change is written as its text, byte for byte. After values change, the text it
    /// writes loads to the same nodes as the stream.
    /// </summary>
    private static void ModelPreservingFormatting(ReadOnlySpan<byte> data)
    {
        var yaml = FuzzText.Decode(data);
        YamlStream stream;
        try
        {
            stream = YamlStream.Load(new StringReader(yaml), LoadOptions);
        }
        catch (YamlException)
        {
            return;
        }

        FuzzAssert.Equal(yaml, stream.ToStringPreservingFormatting(), "An unchanged stream is not written as its text");

        // A node that is written again is written by the emitter, which writes a tag of a %TAG handle in its verbatim
        // form: a prefix that is not a URI, which %TAG accepts, gives a verbatim tag that is not valid
        if (yaml.Contains("%TAG", StringComparison.Ordinal))
            return;

        var changed = 0;
        foreach (var document in stream)
        {
            if (document.Contents is not null)
            {
                changed += ChangeScalars(document.Contents);
            }
        }

        if (changed == 0)
            return;

        var written = stream.ToStringPreservingFormatting();
        YamlStream reloaded;
        try
        {
            reloaded = YamlStream.Load(new StringReader(written), LoadOptions);
        }
        catch (YamlException ex)
        {
            throw new FuzzAssertionException($"The text written after a change cannot be loaded: {FuzzAssert.Format(written)}", ex);
        }

        FuzzAssert.Equal(Represent(stream), Represent(reloaded), $"The text written after a change loads to different nodes: {FuzzAssert.Format(written)}");

        // Every other untagged scalar value of a mapping becomes a new scalar
        static int ChangeScalars(YamlNode node)
        {
            var count = 0;
            switch (node)
            {
                case YamlMapping mapping:
                    var index = 0;
                    foreach (var entry in mapping.ToList())
                    {
                        if (entry.Value is YamlValue { Tag: null } && index++ % 2 == 0)
                        {
                            mapping[entry.Key] = new YamlValue("new value " + index.ToString(CultureInfo.InvariantCulture));
                            count++;
                        }
                        else if (entry.Value is not null)
                        {
                            count += ChangeScalars(entry.Value);
                        }
                    }

                    break;

                case YamlSequence sequence:
                    foreach (var item in sequence)
                    {
                        count += ChangeScalars(item);
                    }

                    break;
            }

            return count;
        }
    }

    // The nodes of the scanner-based parser, in the format of ReadUtf8ReaderNodes
    private static ParserResult ReadParserNodes(string yaml)
    {
        var events = new List<string>();
        try
        {
            var parser = new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml));
            while (parser.MoveNext())
            {
                var current = parser.Current;
                var properties = current is NodeEvent node ? (node.Anchor is null ? "" : " &" + node.Anchor) + (node.Tag is null ? "" : " !" + node.Tag) : "";
                switch (current)
                {
                    case DocumentStart start:
                        events.Add(start.IsImplicit ? "+DOC" : "+DOC ---");
                        break;
                    case DocumentEnd end:
                        events.Add(end.IsImplicit ? "-DOC" : "-DOC ...");
                        break;
                    case MappingStart mapping:
                        events.Add("+MAP" + (mapping.Style is YamlStyle.Flow ? " {}" : "") + properties);
                        break;
                    case MappingEnd:
                        events.Add("-MAP");
                        break;
                    case SequenceStart sequence:
                        events.Add("+SEQ" + (sequence.Style is YamlStyle.Flow ? " []" : "") + properties);
                        break;
                    case SequenceEnd:
                        events.Add("-SEQ");
                        break;
                    case AnchorAlias alias:
                        events.Add("=ALI *" + alias.Value);
                        break;
                    case Scalar scalar:
                        events.Add("=VAL" + properties + " " + scalar.Style.ToString() + " " + EscapeValue(scalar.Value));
                        break;
                }
            }
        }
        catch (YamlException ex)
        {
            return new ParserResult(string.Join('\n', events), ex.Message);
        }

        return new ParserResult(string.Join('\n', events), Error: null);
    }

    private static ParserResult ReadUtf8ReaderNodes(byte[] utf8)
    {
        var events = new List<string>();
        try
        {
            var reader = new Utf8YamlReader(utf8);
            while (reader.Read())
            {
                var properties = (reader.AnchorStart < 0 ? "" : " &" + Encoding.UTF8.GetString(reader.AnchorName)) + (reader.TagStart < 0 ? "" : " !" + reader.GetTag());
                switch (reader.TokenKind)
                {
                    case Utf8YamlTokenKind.DocumentStart:
                        events.Add(reader.TokenLength == 0 ? "+DOC" : "+DOC ---");
                        break;
                    case Utf8YamlTokenKind.DocumentEnd:
                        events.Add(reader.TokenLength == 0 ? "-DOC" : "-DOC ...");
                        break;
                    case Utf8YamlTokenKind.MappingStart:
                        events.Add("+MAP" + (reader.IsFlow ? " {}" : "") + properties);
                        break;
                    case Utf8YamlTokenKind.MappingEnd:
                        events.Add("-MAP");
                        break;
                    case Utf8YamlTokenKind.SequenceStart:
                        events.Add("+SEQ" + (reader.IsFlow ? " []" : "") + properties);
                        break;
                    case Utf8YamlTokenKind.SequenceEnd:
                        events.Add("-SEQ");
                        break;
                    case Utf8YamlTokenKind.Alias:
                        events.Add("=ALI *" + Encoding.UTF8.GetString(reader.AnchorName));
                        break;
                    case Utf8YamlTokenKind.Scalar:
                        var value = reader.GetString();
                        FuzzAssert.True(reader.ValueEquals(Encoding.UTF8.GetBytes(value)), "ValueEquals does not match GetString");
                        events.Add("=VAL" + properties + " " + reader.ScalarStyle.ToString() + " " + EscapeValue(value));
                        break;
                }
            }
        }
        catch (YamlException ex)
        {
            return new ParserResult(string.Join('\n', events), ex.Message);
        }

        return new ParserResult(string.Join('\n', events), Error: null);
    }

    // The tokens, the indicators and the properties do not overlap, and the gaps between them hold only whitespace, line
    // breaks and comments.
    private static string? CheckUtf8ReaderTokenCoverage(byte[] utf8)
    {
        var spans = new List<(int Start, int Length)>();
        var reader = new Utf8YamlReader(utf8);
        while (reader.Read())
        {
            FuzzAssert.True(reader.TokenStart >= 0 && reader.TokenLength >= 0 && reader.TokenStart + reader.TokenLength <= utf8.Length, $"The token {reader.TokenKind} is outside the input");
            AddSpan(spans, reader.TokenStart, reader.TokenLength);
            AddSpan(spans, reader.IndicatorOffset, 1);
            AddSpan(spans, reader.ExplicitKeyIndicatorOffset, 1);
            AddSpan(spans, reader.AnchorStart, reader.AnchorLength);
            AddSpan(spans, reader.TagStart, reader.TagLength);
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var position = 0;
        foreach (var (start, length) in spans)
        {
            if (start < position)
                return string.Create(CultureInfo.InvariantCulture, $"the span at {start} overlaps the previous span, which ends at {position}");

            if (!IsTrivia(utf8.AsSpan(position, start - position)))
                return string.Create(CultureInfo.InvariantCulture, $"the gap {position}..{start} is not trivia");

            position = start + length;
        }

        return IsTrivia(utf8.AsSpan(position)) ? null : string.Create(CultureInfo.InvariantCulture, $"the gap {position}..{utf8.Length} is not trivia");

        static void AddSpan(List<(int Start, int Length)> spans, int start, int length)
        {
            if (start >= 0 && length > 0)
            {
                spans.Add((start, length));
            }
        }

        static bool IsTrivia(ReadOnlySpan<byte> gap)
        {
            for (var i = 0; i < gap.Length; i++)
            {
                switch (gap[i])
                {
                    case (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n':
                        break;

                    case (byte)'#':
                        while (i + 1 < gap.Length && gap[i + 1] is not ((byte)'\r' or (byte)'\n'))
                        {
                            i++;
                        }

                        break;

                    default:
                        return false;
                }
            }

            return true;
        }
    }

    private static void Parser(ReadOnlySpan<byte> data)
    {
        var yaml = FuzzText.Decode(data);

        // The scanner-based parser: a capacity smaller than most tokens exercises the refills of the streaming buffer
        var scanner = ReadEvents(new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml)));
        var actual = ReadEvents(new Parser<LookAheadBuffer>(new LookAheadBuffer(new StringReader(yaml), capacity: 12)));
        FuzzAssert.Equal(scanner, actual, "The string and streaming buffers produce different events");

        // Parser.CreateParser reads a StreamReader by blocks, with Utf8YamlReader. A stream that returns a few bytes per
        // read makes it continue in a new buffer at every position. The byte order mark is kept as a character, like the
        // string parser sees it.
        var expected = ReadEvents(Yaml.Parser.CreateParser(new StringReader(yaml)));
        if (Utf8.IsValid(data))
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            using var reader = new StreamReader(new TrickleStream(data.ToArray()), encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 16);
            FuzzAssert.Equal(expected, ReadEvents(Yaml.Parser.CreateParser(reader)), "The string and StreamReader buffers produce different events");
        }
    }

    private static void SyntaxTree(ReadOnlySpan<byte> data)
    {
        var yaml = FuzzText.Decode(data);
        var parserError = ReadEvents(Yaml.Parser.CreateParser(new StringReader(yaml))).Error;

        YamlSyntaxTree tree;
        try
        {
            tree = YamlSyntaxTree.Parse(yaml);
        }
        catch (YamlException ex)
        {
            FuzzAssert.True(parserError is not null, $"The syntax tree rejects YAML that the parser accepts: {ex.Message}");
            return;
        }

        FuzzAssert.True(parserError is null, $"The syntax tree accepts YAML that the parser rejects: {parserError}");
        FuzzAssert.Equal(yaml, tree.ToFullString(), "The syntax tree does not round-trip");
        var previousStart = 0;
        foreach (var token in tree.Tokens)
        {
            var start = token.Span.Start.Index;
            var end = token.Span.End.Index;
            FuzzAssert.True(start >= 0 && start <= end && end <= yaml.Length, $"The span of the token {token} is outside the text ({yaml.Length} characters)");
            FuzzAssert.True(start >= previousStart, $"The token {token} starts before the previous token");
            FuzzAssert.Equal(yaml[start..end], token.Text, $"The text of the token {token} does not match its span");
            previousStart = start;
        }

        var withoutTrivia = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = false });
        FuzzAssert.False(withoutTrivia.Tokens.Any(token => IsTrivia(token.Kind)), "The syntax tree contains trivia although it is excluded");
        FuzzAssert.Equal(FormatTokens(tree.Tokens.Where(token => !IsTrivia(token.Kind))), FormatTokens(withoutTrivia.Tokens), "Excluding the trivia changes the other tokens");
        FuzzAssert.Equal(tree.Root.Span.ToString(), withoutTrivia.Root.Span.ToString(), "Excluding the trivia changes the span of the root node");
    }

    private static bool IsTrivia(YamlSyntaxKind kind) => kind is YamlSyntaxKind.WhitespaceTrivia or YamlSyntaxKind.NewLineTrivia or YamlSyntaxKind.CommentTrivia;

    private static string FormatTokens(IEnumerable<YamlSyntaxToken> tokens) => string.Join('\n', tokens.Select(token => $"{token.Kind} {token.Span.Start.Index}..{token.Span.End.Index} {token.Text}"));

    private static void Model(ReadOnlySpan<byte> data)
    {
        var yaml = FuzzText.Decode(data);
        YamlStream stream;
        try
        {
            stream = YamlStream.Load(new StringReader(yaml), LoadOptions);
        }
        catch (YamlException)
        {
            return;
        }

        var saved = Save(stream);
        YamlStream reloaded;
        try
        {
            reloaded = YamlStream.Load(new StringReader(saved), LoadOptions);
        }
        catch (YamlException ex)
        {
            throw new FuzzAssertionException($"The saved YAML cannot be loaded: {FuzzAssert.Format(saved)}", ex);
        }

        FuzzAssert.Equal(saved, Save(reloaded), "Saving the reloaded stream gives a different text");
    }

    private static void Deserialize(ReadOnlySpan<byte> data)
    {
        var options = CreateSerializerOptions(FuzzInput.ReadByte(ref data), FuzzInput.ReadByte(ref data));
        var yaml = FuzzText.Decode(data);

        var fromString = Capture(() => YamlSerializer.Deserialize<object>(yaml, options));
        AssertSameResult(fromString, Capture(() => YamlSerializer.Deserialize<object>(new StringReader(yaml), options)), "The string and TextReader overloads disagree");

        var succeeded = YamlSerializer.TryDeserialize<object>(yaml, out var tried, options);
        FuzzAssert.Equal(fromString.Error is null, succeeded, $"TryDeserialize returns {succeeded} while Deserialize reports {fromString.Error ?? "no error"}");
        AssertSameResult(fromString, new DeserializeResult(tried, fromString.Error), "TryDeserialize and Deserialize disagree");

        // The UTF-8 overloads see the raw bytes, invalid sequences included
        var bytes = data.ToArray();
        using (var utf8Stream = new MemoryStream(bytes))
        {
            var fromStream = Capture(() => YamlSerializer.Deserialize<object>(utf8Stream, options));
            if (Utf8.IsValid(data))
            {
                // A stream does not count its byte order mark as a character, which shifts the character offsets of the errors
                var expected = yaml is ['\uFEFF', ..] ? Capture(() => YamlSerializer.Deserialize<object>(yaml[1..], options)) : fromString;
                AssertSameResult(expected, fromStream, "The string and Stream overloads disagree");
                AssertSameResult(fromString, Capture(() => YamlSerializer.Deserialize<object>(bytes.AsSpan(), options)), "The string and UTF-8 bytes overloads disagree");
            }

            // A stream that returns a few bytes per read makes the reader continue in a new buffer at every position
            AssertSameResult(fromStream, Capture(() => YamlSerializer.Deserialize<object>(new TrickleStream(bytes), options)), "A stream read by a few bytes gives another result");
        }

        _ = YamlSerializer.TryDeserialize<Dictionary<string, object?>>(yaml, out _, options);

        if (fromString.Error is not null)
            return;

        // ReferenceHandling.Preserve writes an anchor on every collection, which AllowAnchors would reject
        var serialized = YamlSerializer.Serialize(fromString.Value, options);
        object? reloaded;
        try
        {
            reloaded = YamlSerializer.Deserialize<object>(serialized, options with { AllowAnchors = true, AllowAliases = true });
        }
        catch (YamlException ex)
        {
            throw new FuzzAssertionException($"The serialized value cannot be deserialized: {FuzzAssert.Format(serialized)}", ex);
        }

        // Without PreferQuotedForAmbiguousScalars, a string that reads as another type, such as "1", is written plain.
        // The failsafe schema has no null: the null read from an empty stream is written as "null", a string there.
        if (options.ScalarStylePreferences.PreferQuotedForAmbiguousScalars && !(fromString.Value is null && options is { UseSchema: true, Schema: YamlSchemaKind.Failsafe }))
        {
            FuzzAssert.True(StructuralEquals(fromString.Value, reloaded, new(ReferenceEqualityComparer.Instance)), $"The serialized value deserializes to a different value: {FuzzAssert.Format(serialized)} {Describe(fromString.Value)} != {Describe(reloaded)}");
        }
    }

    /// <summary>
    /// The first byte selects the reading options, the second one the writing options. Zero selects the default options,
    /// so the seeds of the other targets prefixed by two zero bytes exercise the default paths.
    /// </summary>
    private static YamlSerializerOptions CreateSerializerOptions(byte read, byte write)
    {
        return LoadOptions with
        {
            Schema = (YamlSchemaKind)(read & 0b11),
            UseSchema = (read & 0b100) is not 0,
            DuplicateKeyHandling = (YamlDuplicateKeyHandling)(((read >> 3) & 0b11) % 3),
            AllowAnchors = (read & 0b10_0000) is 0,
            AllowAliases = (read & 0b100_0000) is 0,
            ReferenceHandling = (YamlReferenceHandling)(((read >> 7) | ((write >> 6) & 0b10)) % 3),
            WriteIndented = (write & 0b1) is 0,
            IndentBlockSequences = (write & 0b10) is 0,
            ScalarStylePreferences = new YamlScalarStylePreferences
            {
                PreferPlainStyle = (write & 0b100) is 0,
                PreferQuotedForAmbiguousScalars = (write & 0b1000) is 0,
                StringStyle = (ScalarStyle)(((write >> 4) & 0b111) % 6),
            },
        };
    }

    private static DeserializeResult Capture(Func<object?> deserialize)
    {
        try
        {
            return new DeserializeResult(deserialize(), Error: null);
        }
        catch (YamlException ex)
        {
            return new DeserializeResult(Value: null, ex.Message);
        }
    }

    private static void AssertSameResult(DeserializeResult expected, DeserializeResult actual, string message)
    {
        FuzzAssert.Equal(expected.Error, actual.Error, message);
        FuzzAssert.True(StructuralEquals(expected.Value, actual.Value, new(ReferenceEqualityComparer.Instance)), $"{message}: {Describe(expected.Value)} != {Describe(actual.Value)}");
    }

    /// <summary>
    /// Compares two values read as <see cref="object"/>: mappings, sequences, and scalars. With ReferenceHandling, an
    /// alias shares the anchored value, possibly in a cycle: the comparison maps each container of one graph to a
    /// container of the other, so both graphs must have the same shape.
    /// </summary>
    private static bool StructuralEquals(object? left, object? right, Dictionary<object, object> visited)
    {
        switch (left, right)
        {
            case (null, null):
                return true;

            case (Dictionary<object, object?> or List<object?>, Dictionary<object, object?> or List<object?>):
                if (visited.TryGetValue(left, out var mapped))
                    return ReferenceEquals(mapped, right);

                visited.Add(left, right);
                return (left, right) switch
                {
                    (List<object?> leftList, List<object?> rightList) => leftList.Count == rightList.Count && leftList.Zip(rightList).All(item => StructuralEquals(item.First, item.Second, visited)),
                    (Dictionary<object, object?> leftDictionary, Dictionary<object, object?> rightDictionary) => leftDictionary.Count == rightDictionary.Count && leftDictionary.All(item => rightDictionary.TryGetValue(item.Key, out var value) && StructuralEquals(item.Value, value, visited)),
                    _ => false,
                };

            case (byte[] leftBytes, byte[] rightBytes):
                return leftBytes.AsSpan().SequenceEqual(rightBytes);

            case (not null, not null):
                return left.GetType() == right.GetType() && left.Equals(right);

            default:
                return false;
        }
    }

    private static string Describe(object? value)
    {
        var builder = new StringBuilder();
        Append(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return builder.ToString();

        void Append(object? value, HashSet<object> path)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;

                case Dictionary<object, object?> or List<object?> when !path.Add(value):
                    builder.Append("<cycle>");
                    break;

                case List<object?> list:
                    builder.Append('[');
                    foreach (var item in list)
                    {
                        Append(item, path);
                        builder.Append(", ");
                    }

                    builder.Append(']');
                    path.Remove(value);
                    break;

                case Dictionary<object, object?> dictionary:
                    builder.Append('{');
                    foreach (var item in dictionary)
                    {
                        Append(item.Key, path);
                        builder.Append(": ");
                        Append(item.Value, path);
                        builder.Append(", ");
                    }

                    builder.Append('}');
                    path.Remove(value);
                    break;

                default:
                    builder.Append(CultureInfo.InvariantCulture, $"({value.GetType().Name}){FuzzAssert.Format(value)}");
                    break;
            }
        }
    }

    private static void EmitterRoundTrip(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var yaml = FuzzText.Decode(data);
        List<ParsingEvent> events;
        try
        {
            events = ParseEvents(yaml);
        }
        catch (YamlException)
        {
            return;
        }

        var isCanonical = (flags & 0b1) is not 0;
        var forceIndentLess = (flags & 0b10) is not 0;
        var emitKeyQuoted = (flags & 0b100) is not 0;
        var bestIndent = 2 + ((flags >> 3) & 0b111);
        var bestWidth = (flags & 0b100_0000) is 0 ? int.MaxValue : (bestIndent * 2) + 1 + (flags >> 7);

        string emitted;
        using (var writer = new StringWriter { NewLine = "\n" })
        {
            var emitter = new Emitter(writer, bestIndent, bestWidth, isCanonical, forceIndentLess, emitKeyQuoted);
            foreach (var @event in events)
            {
                emitter.Emit(@event);
            }

            emitted = writer.ToString();
        }

        List<ParsingEvent> reparsed;
        try
        {
            reparsed = ParseEvents(emitted);
        }
        catch (YamlException ex)
        {
            throw new FuzzAssertionException($"The emitted YAML cannot be parsed: {FuzzAssert.Format(emitted)}", ex);
        }

        FuzzAssert.Equal(DescribeEvents(events, isCanonical, emitKeyQuoted), DescribeEvents(reparsed, isCanonical, emitKeyQuoted), $"The emitted YAML parses to different events: {FuzzAssert.Format(emitted)}");
    }

    private static List<ParsingEvent> ParseEvents(string yaml)
    {
        var events = new List<ParsingEvent>();
        var parser = Yaml.Parser.CreateParser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            events.Add(parser.Current!);
        }

        return events;
    }

    /// <summary>
    /// The emitter quotes a plain scalar it cannot write plain, such as a multi-line one or one starting with an
    /// indicator, which only keeps its meaning when it resolves to a string. The empty plain scalar, a null, stays plain:
    /// as a key, the emitter writes it as an explicit key. The canonical form double-quotes every scalar, as
    /// emitKeyQuoted does for the keys, and the events of the parser hold no resolved tag the emitter could write instead.
    /// </summary>
    private static string ResolveTag(Scalar scalar, bool isQuoted)
    {
        if (isQuoted)
            return "untagged";

        return Schemas.CoreSchema.Instance.TryParse(scalar, decodeValue: false, out var tag, out _) ? tag : "?";
    }

    /// <summary>
    /// The emitter reads a tag that starts with a named handle whose prefix is global, such as "!!str", as a shorthand,
    /// so that a node can be given the tag "!!str" instead of "tag:yaml.org,2002:str". The parser resolves every tag,
    /// but a verbatim local tag, such as <c>!&lt;!!a&gt;</c>, looks like a shorthand: it is expanded the same way here,
    /// unless a %TAG prefix that is a local tag produces it.
    /// </summary>
    private static string? NormalizeTag(string? tag, TagDirective[] directives)
    {
        if (tag is not ['!', ..] || directives.Any(directive => directive.Prefix is ['!', _, ..] && tag.Length > directive.Prefix.Length && tag.StartsWith(directive.Prefix, StringComparison.Ordinal)))
            return tag;

        var shorthand = directives.FirstOrDefault(directive => directive.Handle.Length > 1 && directive.Prefix is [not '!', ..] && tag.Length > directive.Handle.Length && tag.StartsWith(directive.Handle, StringComparison.Ordinal));
        return shorthand is null ? tag : string.Concat(shorthand.Prefix, tag.AsSpan(shorthand.Handle.Length));
    }

    /// <summary>
    /// Describes the semantic part of the events. The presentation may change (scalar and collection styles, document
    /// markers, directives, positions), but not the nodes, their anchors and tags, and the tag an untagged scalar
    /// resolves to.
    /// </summary>
    private static string DescribeEvents(List<ParsingEvent> events, bool isCanonical, bool emitKeyQuoted)
    {
        var builder = new StringBuilder();

        // The number of nodes read in each open collection, negated for a mapping: an even count in a mapping is a key
        var collections = new Stack<int>();
        var directives = DefaultTagDirectives;
        foreach (var @event in events)
        {
            if (@event is DocumentStart documentStart)
            {
                TagDirective[] documentDirectives = [.. documentStart.Tags ?? Enumerable.Empty<TagDirective>()];
                directives = [.. documentDirectives, .. DefaultTagDirectives.Where(directive => !documentDirectives.Any(item => item.Handle == directive.Handle))];
            }

            var isKey = collections.TryPeek(out var count) && count <= 0 && count % 2 is 0;
            if (@event is NodeEvent or AnchorAlias && collections.Count > 0)
            {
                collections.Push(collections.Pop() + (count <= 0 ? -1 : 1));
            }

            switch (@event)
            {
                case SequenceStart:
                    collections.Push(1);
                    break;
                case MappingStart:
                    collections.Push(0);
                    break;
                case SequenceEnd or MappingEnd:
                    collections.Pop();
                    break;
            }

            var description = @event switch
            {
                Scalar scalar => $"=VAL &{scalar.Anchor} <{NormalizeTag(scalar.Tag, directives) ?? ResolveTag(scalar, isCanonical || (emitKeyQuoted && isKey))}> {scalar.Value}",
                SequenceStart sequence => $"+SEQ &{sequence.Anchor} <{NormalizeTag(sequence.Tag, directives)}>",
                MappingStart mapping => $"+MAP &{mapping.Anchor} <{NormalizeTag(mapping.Tag, directives)}>",
                AnchorAlias alias => $"=ALI *{alias.Value}",
                SequenceEnd => "-SEQ",
                MappingEnd => "-MAP",
                DocumentStart => "+DOC",
                DocumentEnd => "-DOC",
                StreamStart => "+STR",
                StreamEnd => "-STR",
                _ => @event.GetType().Name,
            };

            builder.Append(description).Append('\n');
        }

        return builder.ToString();
    }

    private static string Save(YamlStream stream)
    {
        using var writer = new StringWriter { NewLine = "\n" };
        stream.WriteTo(writer);
        return writer.ToString();
    }

    private static ParserResult ReadEvents(IParser parser)
    {
        var events = new List<string>();
        try
        {
            while (parser.MoveNext())
            {
                var current = parser.Current!;
                events.Add($"{current} [{FormatMark(current.Start)}..{FormatMark(current.End)}]");
            }
        }
        catch (YamlException ex)
        {
            FuzzAssert.True(ex.Start.Index >= 0 && ex.End.Index >= ex.Start.Index, $"Invalid error location {ex.Start}..{ex.End}: {ex.Message}");
            return new ParserResult(string.Join('\n', events), ex.Message);
        }

        return new ParserResult(string.Join('\n', events), Error: null);
    }

    private static string FormatMark(Mark mark) => string.Create(CultureInfo.InvariantCulture, $"{mark.Index}:{mark.Line}:{mark.Column}");

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        foreach (var (id, yaml) in GetSuiteCases())
        {
            yield return FuzzSeed.FromText(id, yaml);
        }
    }

    /// <summary>Each case with the default options, and with options that vary from one case to the next.</summary>
    /// <summary>
    /// Byte 0: the options. Then a document read into members of the complex and floating-point types. A document that is
    /// read must be written, and the written YAML must read back to values that write the same YAML.
    /// </summary>
    private static void Numerics(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.Pick(NumericsOptions, FuzzInput.ReadByte(ref data));
        var yaml = FuzzText.Decode(data);

        NumericsPayload? payload;
        try
        {
            payload = YamlSerializer.Deserialize<NumericsPayload>(yaml, options);
        }
        catch (YamlException)
        {
            return;
        }

        if (payload is null)
            return;

        var written = YamlSerializer.Serialize(payload, options);
        NumericsPayload? roundTrip;
        try
        {
            roundTrip = YamlSerializer.Deserialize<NumericsPayload>(written, options);
        }
        catch (YamlException ex)
        {
            FuzzAssert.Fail($"The written YAML cannot be read back: {ex.Message}\n{written}");
            return;
        }

        FuzzAssert.Equal(written, YamlSerializer.Serialize(roundTrip, options), "The written values do not read back to the same values");
    }

    private static IEnumerable<FuzzSeed> GetNumericsSeeds()
    {
        string[] documents =
        [
            "Complex:\n  Real: 1.5\n  Imaginary: -2.0\n",
            "complex: {real: 3, imaginary: 4}\n",
            "Complex: {Real: 3.0, Imaginary: 4.0, Magnitude: 5.0, Phase: 0.93}\n",
            "Complex: {IMAGINARY: 2}\n",
            "Optional: {Real: .nan, Imaginary: -.inf}\n",
            "Optional: null\n",
            "Values:\n  - {Real: 1, Imaginary: 2}\n  - &a {Real: 3, Imaginary: 4}\n  - *a\n",
            "Complex: 1.5\n",
            "Complex: {Real: abc}\n",
            "Ratio: 1_000.5\n",
            "BFloat16: 1.5\nDecimal32: -5.30\nDecimal64: 123.456\nDecimal128: 0.1000000000000000000000000000000001\n",
            "Decimal64: .inf\nDecimal128: 1E+400\nDecimal32: 9.999999E+96\n",
            "ComplexDouble: {Real: 0.5, Imaginary: 0.25}\nComplexHalf: {Real: 1.0, Imaginary: -.inf}\nComplexDecimal: {Real: 1.50, Imaginary: .nan}\n",
        ];

        for (var i = 0; i < documents.Length; i++)
        {
            yield return FuzzSeed.FromText("numerics-" + i.ToString(CultureInfo.InvariantCulture), [(byte)i], documents[i]);
        }
    }

    private sealed class NumericsPayload
    {
        public System.Numerics.Complex Complex { get; set; }

        public System.Numerics.Complex? Optional { get; set; }

        public List<System.Numerics.Complex>? Values { get; set; }

        public Half Ratio { get; set; }
#if NET11_0_OR_GREATER

        public System.Numerics.BFloat16 BFloat16 { get; set; }

        public System.Numerics.Decimal32 Decimal32 { get; set; }

        public System.Numerics.Decimal64? Decimal64 { get; set; }

        public System.Numerics.Decimal128 Decimal128 { get; set; }

        public System.Numerics.Complex<double> ComplexDouble { get; set; }

        public System.Numerics.Complex<Half> ComplexHalf { get; set; }

        public System.Numerics.Complex<System.Numerics.Decimal64>? ComplexDecimal { get; set; }
#endif
    }

    private static IEnumerable<FuzzSeed> GetDeserializeSeeds()
    {
        var index = 0;
        foreach (var (id, yaml) in GetSuiteCases())
        {
            yield return FuzzSeed.FromText(id, [0, 0], yaml);
            yield return FuzzSeed.FromText(id + "-options", [(byte)(index * 37), (byte)(index * 101)], yaml);
            index++;
        }
    }

    /// <summary>
    /// Each case with the default emitter, and with settings that vary from one case to the next. The outputs of the
    /// emitters of the suite (out.yaml and emit.yaml) are inputs too: they are the same data in another presentation.
    /// </summary>
    private static IEnumerable<FuzzSeed> GetEmitterSeeds()
    {
        var index = 0;
        foreach (var (id, yaml) in GetSuiteCases().Concat(GetSuiteEmitterOutputs()))
        {
            yield return FuzzSeed.FromText(id, [0], yaml);
            yield return FuzzSeed.FromText(id + "-options", [(byte)(index * 59)], yaml);
            index++;
        }
    }

    private static IEnumerable<(string Id, string Yaml)> GetSuiteEmitterOutputs()
    {
        using var document = FuzzResources.ReadJson(typeof(YamlFuzzTargets).Assembly, "yaml-test-suite.cases.json");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            foreach (var propertyName in new[] { "out", "emit" })
            {
                if (item.TryGetProperty(propertyName, out var value))
                {
                    yield return (id + "-" + propertyName, value.GetString()!);
                }
            }
        }
    }

    private static IEnumerable<(string Id, string Yaml)> GetSuiteCases()
    {
        using var document = FuzzResources.ReadJson(typeof(YamlFuzzTargets).Assembly, "yaml-test-suite.cases.json");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            yield return (item.GetProperty("id").GetString()!, item.GetProperty("yaml").GetString()!);
        }
    }

    private sealed record ParserResult(string Events, string? Error);

    private sealed record DeserializeResult(object? Value, string? Error);

    /// <summary>A read-only stream that returns between 1 and 3 bytes per read.</summary>
    private sealed class TrickleStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var length = Math.Min(Math.Min(buffer.Length, 1 + (_position % 3)), data.Length - _position);
            data.AsSpan(_position, length).CopyTo(buffer);
            _position += length;
            return length;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
