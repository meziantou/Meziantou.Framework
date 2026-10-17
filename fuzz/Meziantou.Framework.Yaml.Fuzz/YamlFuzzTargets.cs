using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Syntax;

namespace Meziantou.Framework.Yaml.Fuzz;

/// <summary>
/// Malformed YAML must only throw <see cref="YamlException"/>. The two look-ahead buffers of the parser must produce the
/// same events, the syntax tree must accept exactly what the parser accepts, and a loaded stream must save to YAML that
/// loads back to the same text.
/// </summary>
public static class YamlFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "---", "...", "%YAML 1.2", "%YAML 1.1", "%TAG ! tag:example.com,2000:", "%TAG !e! tag:e,2000:", "!", "!!", "!!str", "!!int", "!!float", "!!bool", "!!null", "!!map", "!!seq", "!!set", "!!omap", "!!binary",
        "!<tag:yaml.org,2002:str>", "!e!local", "&a ", "*a", "<<", "? ", ": ", "- ", "-", ":", "?", "|", ">", "|-", "|+", ">-", ">+", "|2", ">1-", "{", "}", "[", "]", ", ", "#", " #", "'", "''", "\"", "\\", "\\u263A", "\\x0d", "\\N", "\\_", "\\L", "\\P", "\\\n",
        "\t", "\n", "\r\n", "\r", "\u0085", "\u2028", "\uFEFF", "  ", "null", "~", "true", "false", "yes", ".inf", "-.inf", ".nan", "0x1F", "0o17", "1e3", "2001-12-14t21:59:43.10-05:00",
    ];

    private static readonly YamlSerializerOptions LoadOptions = new() { MaxDepth = 64, MaxAliasExpansionNodeCount = 10_000 };

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("yaml-parser", Parser) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-syntax-tree", SyntaxTree) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-model", Model) { Seeds = GetSeeds, Dictionary = Tokens },
        new("yaml-deserialize", Deserialize) { Seeds = GetSeeds, Dictionary = Tokens },
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
    }

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
        var yaml = FuzzText.Decode(data);
        _ = YamlSerializer.TryDeserialize<object>(yaml, out _, LoadOptions);
        _ = YamlSerializer.TryDeserialize<Dictionary<string, object?>>(yaml, out _, LoadOptions);

        // The UTF-8 overload sees the raw bytes, invalid sequences included
        try
        {
            using var utf8Stream = new MemoryStream(data.ToArray());
            _ = YamlSerializer.Deserialize<object>(utf8Stream, LoadOptions);
        }
        catch (YamlException)
        {
        }
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
        using var document = FuzzResources.ReadJson(typeof(YamlFuzzTargets).Assembly, "yaml-test-suite.cases.json");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            yield return FuzzSeed.FromText(item.GetProperty("id").GetString()!, item.GetProperty("yaml").GetString()!);
        }
    }

    private sealed record ParserResult(string Events, string? Error);
}
