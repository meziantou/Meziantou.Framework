using System.Globalization;
using System.Text;
using System.Text.Unicode;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Model;
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

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("yaml-parser", Parser) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-syntax-tree", SyntaxTree) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-model", Model) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-deserialize", Deserialize) { Seeds = GetDeserializeSeeds, Dictionary = Tokens },
        new("yaml-emitter", EmitterRoundTrip) { Seeds = GetEmitterSeeds, Dictionary = Tokens },
    ];

    private static void Parser(ReadOnlySpan<byte> data)
    {
        var yaml = FuzzText.Decode(data);
        var expected = ReadEvents(Yaml.Parser.CreateParser(new StringReader(yaml)));

        // A capacity smaller than most tokens exercises the refills of the streaming buffer
        var actual = ReadEvents(new Parser<LookAheadBuffer>(new LookAheadBuffer(new StringReader(yaml), capacity: 12)));
        FuzzAssert.Equal(expected, actual, "The string and streaming buffers produce different events");
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

        // The UTF-8 overload sees the raw bytes, invalid sequences included
        using (var utf8Stream = new MemoryStream(data.ToArray()))
        {
            var fromStream = Capture(() => YamlSerializer.Deserialize<object>(utf8Stream, options));
            if (Utf8.IsValid(data))
            {
                // The stream reader removes the byte order mark, which shifts the character offsets of the errors
                var expected = yaml is ['\uFEFF', ..] ? Capture(() => YamlSerializer.Deserialize<object>(yaml[1..], options)) : fromString;
                AssertSameResult(expected, fromStream, "The string and Stream overloads disagree");
            }
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
    /// indicator, which only keeps its meaning when it resolves to a string. The empty plain scalar is the exception:
    /// it is null, and the emitter writes it as <c>''</c> in a flow collection or a key, where it cannot be plain. The
    /// canonical form double-quotes every scalar, as emitKeyQuoted does for the keys, and the events of the parser hold
    /// no resolved tag the emitter could write instead.
    /// </summary>
    private static string ResolveTag(Scalar scalar, bool isQuoted)
    {
        if (isQuoted)
            return "untagged";

        if (scalar.Value.Length is 0)
            return "empty";

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
                events.Add($"{current} [{current.Start.Index}..{current.End.Index}]");
            }
        }
        catch (YamlException ex)
        {
            FuzzAssert.True(ex.Start.Index >= 0 && ex.End.Index >= ex.Start.Index, $"Invalid error location {ex.Start}..{ex.End}: {ex.Message}");
            return new ParserResult(string.Join('\n', events), ex.Message);
        }

        return new ParserResult(string.Join('\n', events), Error: null);
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        foreach (var (id, yaml) in GetSuiteCases())
        {
            yield return FuzzSeed.FromText(id, yaml);
        }
    }

    /// <summary>Each case with the default options, and with options that vary from one case to the next.</summary>
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

    /// <summary>Each case with the default emitter, and with settings that vary from one case to the next.</summary>
    private static IEnumerable<FuzzSeed> GetEmitterSeeds()
    {
        var index = 0;
        foreach (var (id, yaml) in GetSuiteCases())
        {
            yield return FuzzSeed.FromText(id, [0], yaml);
            yield return FuzzSeed.FromText(id + "-options", [(byte)(index * 59)], yaml);
            index++;
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
}
