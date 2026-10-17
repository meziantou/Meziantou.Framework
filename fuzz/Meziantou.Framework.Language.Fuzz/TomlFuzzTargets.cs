using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// TOML 1.0 and 1.1 documents: the shared invariants, the values of the scalars (which only throw for tokens built by
/// hand, never for parsed ones), the grouping of the properties under their table, and the key/value pairs and their lookup.
/// </summary>
internal static class TomlFuzzTargets
{
    private const int KeyValueSampleCount = 8;
    private const int MaxKeyValues = 512;

    private static readonly string[] Tokens =
    [
        "[", "]", "[[", "]]", "[a.b]", "[[arr]]", "=", " = ", ".", ",", "{", "}", "{ a = 1 }", "\"", "'", "\"\"\"", "'''", "\\", "\\n", "\\t", "\\u00E9", "\\U0001F600", "\\e", "\\x41", "\\\n",
        "#", "\n", "\r\n", "\r", "\t", " ", "key", "\"quoted key\"", "'literal key'", "a.b.c", "true", "false", "inf", "+inf", "-inf", "nan", "+nan",
        "0", "-0", "+1", "1_000", "0x1F", "0o17", "0b101", "1.5", "1e10", "6.626e-34", "1_0.0_1", "9223372036854775807", "-9223372036854775808", "9223372036854775808",
        "1979-05-27", "07:32:00", "07:32", "1979-05-27T07:32:00Z", "1979-05-27 07:32:00.999999-07:00", "1979-05-27T07:32:00", "T", "Z", "+05:30",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-toml", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>Byte 0: bit 0 reads TOML 1.0 instead of 1.1. Byte 1: the nesting limit. Then the document.</summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var depth = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        var options = new TomlParseOptions
        {
            Version = (flags & 1) == 0 ? TomlVersion.V1_1 : TomlVersion.V1_0,
            MaxDepth = LanguageFuzzInput.ReadDepth(depth),
        };

        var tree = TomlSyntaxTree.ParseText(text, options);

        // The keys and tables defined twice are reported by a pass over the document, not by the nodes
        var shape = SyntaxTreeInvariants.Check(tree, text, "TOML", diagnosticsOnNodes: false, diagnosticsInSourceOrder: true);
        var root = tree.GetRoot();
        FuzzAssert.Equal(text, root.Entries.ToFullString() + root.EndOfFileToken.ToFullString(), "The entries and the end of the file do not reproduce the text");
        FuzzAssert.Equal(Describe(tree.GetDiagnostics()), Describe(tree.GetDiagnostics(root)), "The diagnostics of the root are not the diagnostics of the tree");

        CheckValues(root);
        CheckTables(root);
        CheckKeyValues(root);

        var walker = new CountingWalker();
        try
        {
            walker.Visit(root);
            SyntaxTreeInvariants.CheckWalker(shape, walker.TokenCount, walker.TriviaCount);
        }
        catch (InsufficientExecutionStackException)
        {
            // Documented by the walker for a tree nested too deeply to walk
        }

        static string Describe(IEnumerable<Diagnostic> diagnostics)
            => string.Join('\n', diagnostics.Select(diagnostic => $"{diagnostic.Id} {diagnostic.Location.SourceSpan} {diagnostic.Message}"));
    }

    private static void CheckValues(TomlDocumentSyntax root)
    {
        // The values only throw InvalidOperationException for a token built by hand: the parser only makes a scalar of a
        // token it could read, and turns any other into a bad token.
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case TomlIntegerSyntax integer:
                    FuzzAssert.Equal(integer.IntegerToken.ValueText, integer.Value.ToString(CultureInfo.InvariantCulture), $"The integer {FuzzAssert.Format(integer.IntegerToken.Text)} does not have the value of its token");
                    break;

                case TomlFloatSyntax number:
                    _ = number.Value;
                    break;

                case TomlDateTimeSyntax dateTime:
                    FuzzAssert.True(dateTime.Value is DateTimeOffset or DateTime or DateOnly or TimeOnly, $"The date or time {FuzzAssert.Format(dateTime.DateTimeToken.Text)} has a value of type {dateTime.Value.GetType()}");
                    break;

                case TomlStringSyntax value:
                    _ = value.Value;
                    _ = value.IsMultiLine;
                    _ = value.IsLiteral;
                    break;

                case TomlBooleanSyntax boolean:
                    _ = boolean.Value;
                    break;

                case TomlKeySyntax key:
                    FuzzAssert.Equal(key.Parts.Count, key.Names.Count, $"The key {FuzzAssert.Format(key.ToString())} does not have a name for each part");
                    _ = key.IsDotted;
                    break;
            }
        }
    }

    /// <summary>Checks that the properties are grouped under the table header before them.</summary>
    private static void CheckTables(TomlDocumentSyntax root)
    {
        var rootProperties = new List<TomlPropertySyntax>();
        var tables = new List<(TomlTableSyntax Table, List<TomlPropertySyntax> Properties)>();
        foreach (var entry in root.Entries)
        {
            switch (entry)
            {
                case TomlTableSyntax table:
                    _ = table.IsArrayOfTables;
                    tables.Add((table, []));
                    break;

                case TomlPropertySyntax property:
                    (tables.Count == 0 ? rootProperties : tables[^1].Properties).Add(property);
                    break;
            }
        }

        FuzzAssert.True(root.RootProperties.SequenceEqual(rootProperties, ReferenceEqualityComparer.Instance), "RootProperties are not the properties before the first table header");
        FuzzAssert.True(root.Tables.SequenceEqual(tables.Select(item => item.Table), ReferenceEqualityComparer.Instance), "Tables are not the table headers of the entries");
        foreach (var (table, properties) in tables)
        {
            FuzzAssert.True(table.Properties.SequenceEqual(properties, ReferenceEqualityComparer.Instance), $"The properties of the table {FuzzAssert.Format(table.Key.ToString())} are not the properties up to the next header");
        }
    }

    private static void CheckKeyValues(TomlDocumentSyntax root)
    {
        var pairs = new List<TomlKeyValue>();
        foreach (var pair in root.GetKeyValues())
        {
            FuzzAssert.Equal(pair.Parts.Count, pair.Names.Count, $"The pair {FuzzAssert.Format(pair.ToString())} does not have a name for each part");
            for (var i = 0; i < pair.Names.Count; i++)
            {
                FuzzAssert.Equal(pair.Parts[i].ValueText, pair.Names[i], $"The name {i} of the pair {FuzzAssert.Format(pair.ToString())} is not the value of its part");
            }

            FuzzAssert.True(pair.Names.Count > 0, "A pair has no name");
            pairs.Add(pair);
            if (pairs.Count >= MaxKeyValues)
                break;
        }

        // GetValue looks for the first pair with the names, so it finds one for the names of any pair
        var step = Math.Max(1, pairs.Count / KeyValueSampleCount);
        for (var i = 0; i < pairs.Count; i += step)
        {
            var names = pairs[i].Names.ToArray();
            var value = root.GetValue(names);
            FuzzAssert.True(value is not null, $"GetValue({string.Join(", ", names.Select(FuzzAssert.Format))}) does not find the pair {i}");
            var first = pairs.Find(pair => pair.Names.SequenceEqual(names, StringComparer.Ordinal));
            FuzzAssert.True(first is not null && ReferenceEquals(first.Value, value), $"GetValue({string.Join(", ", names.Select(FuzzAssert.Format))}) is not the value of the first pair with these names");
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        using var document = FuzzResources.ReadJson(typeof(TomlFuzzTargets).Assembly, "toml-test.cases.json");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var versions = item.GetProperty("versions").EnumerateArray().Select(version => version.GetString()).ToList();

            // Bit 0 reads TOML 1.0, for the cases that are only valid or invalid in it
            byte flags = versions.Contains("1.1.0") ? (byte)0 : (byte)1;
            if (item.TryGetProperty("toml", out var toml))
            {
                yield return FuzzSeed.FromText(name, [flags, 0], toml.GetString()!);
            }
            else
            {
                // The cases that are not valid UTF-8 keep their bytes, which FuzzText reads as unpaired surrogates
                yield return new FuzzSeed(name, (byte[])[flags, 0, .. Convert.FromBase64String(item.GetProperty("tomlBase64").GetString()!)]);
            }
        }

        yield return FuzzSeed.FromText("depth", [0, 1], "a = [[1], [[2]], { b = { c = [3] } }]\n[t]\nx = { y = [ { z = 1 } ] }\n");
    }

    private sealed class CountingWalker() : TomlSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int TokenCount { get; private set; }

        public int TriviaCount { get; private set; }

        public override void VisitToken(SyntaxToken token)
        {
            TokenCount++;
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia) => TriviaCount++;
    }
}
