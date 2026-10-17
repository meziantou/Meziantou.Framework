using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using LanguageToml = Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.Toml.Fuzz;

/// <summary>
/// Malformed TOML must only throw <see cref="TomlException"/>. The syntax tree must round-trip any text, the strict and
/// tolerant event parsers must agree, a deserialized model must serialize to TOML that deserializes to the same model,
/// and every pipeline must agree with <c>Meziantou.Framework.Language.Toml</c>, an independent implementation, about
/// which documents are valid.
/// </summary>
public static class TomlFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "=", " = ", "[", "]", "[[", "]]", "{", "}", "{ ", " }", ",", ", ", ".", "\"", "'", "\"\"\"", "'''", "#", " # ", "\\", "\\u00E9", "\\U0001F600", "\\x41", "\\e", "\\n", "\\t", "\\\"", "\\\n",
        "\n", "\r\n", "\r", "\t", " ", "\uFEFF", "true", "false", "inf", "+inf", "-inf", "nan", "+nan", "-nan", "0x", "0o", "0b", "0xDEAD_BEEF", "0o755", "0b1101", "1_000", "+1", "-1", "1e10", "1E-5", "3.14", "-0.0", "9223372036854775807", "-9223372036854775808",
        "1979-05-27", "07:32:00", "07:32", "1979-05-27T07:32:00Z", "1979-05-27 07:32:00", "1979-05-27T00:32:00.999999-07:00", "1979-05-27T07:32:00.5+01:30", "T", "Z",
        "a", "a.b", "\"a\".b", "'a'.b", "[a]", "[a.b]", "[[a]]", "[[a.b]]", "a = 1", "a = []", "a = {}", "a.b = 1", "x = { y = 1 }",
    ];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly LanguageToml.TomlParseOptions LanguageTomlOptions = new() { Version = LanguageToml.TomlVersion.V1_1 };

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("toml-syntax", Syntax) { Seeds = () => GetSeeds(prefix: []), Dictionary = Tokens },
        new("toml-parser", Parser) { Seeds = () => GetSeeds(prefix: [0]), Dictionary = Tokens },
        new("toml-deserialize", Deserialize) { Seeds = () => GetSeeds(prefix: []), Dictionary = Tokens },
        new("toml-differential", Differential) { Seeds = () => GetSeeds(prefix: []), Dictionary = Tokens },
    ];

    private static void Syntax(ReadOnlySpan<byte> data)
    {
        var toml = FuzzText.Decode(data);
        var doc = SyntaxParser.Parse(toml);
        var roundtrip = doc.ToString();
        FuzzAssert.Equal(toml, roundtrip, "The syntax tree does not round-trip");

        using (var reader = new StringReader(toml))
        {
            var docFromReader = SyntaxParser.Parse(reader);
            FuzzAssert.Equal(roundtrip, docFromReader.ToString(), "The TextReader overload gives a different tree");
            FuzzAssert.Equal(doc.HasErrors, docFromReader.HasErrors, "The TextReader overload reports different errors");
        }

        try
        {
            _ = SyntaxParser.ParseStrict(toml);
            FuzzAssert.False(doc.HasErrors, "ParseStrict accepts a document that Parse reports errors for");
        }
        catch (TomlException ex)
        {
            FuzzAssert.True(doc.HasErrors, $"ParseStrict rejects a document that Parse accepts: {ex.Message}");
        }

        var lexer = TomlLexer.Create(toml);
        var tokenCount = 0;
        var previousStart = -1;
        while (lexer.MoveNext())
        {
            var token = lexer.Current;
            tokenCount++;
            FuzzAssert.True(tokenCount <= (2 * toml.Length) + 16, "The lexer does not make progress");
            FuzzAssert.True(token.Start.Offset >= previousStart, $"The token {token} starts before the previous token");
            FuzzAssert.True(token.Start.Offset >= 0 && token.Start.Offset <= toml.Length, $"The token {token} is outside the text ({toml.Length} characters)");
            previousStart = token.Start.Offset;
        }
    }

    private static void Parser(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var toml = FuzzText.Decode(data);
        var options = new TomlParserOptions
        {
            DecodeScalars = (flags & 1) != 0,
            EagerStringValues = (flags & 2) != 0,
            CaptureTrivia = (flags & 4) != 0,
        };

        var tolerant = ReadEvents(TomlParser.Create(toml, options with { Mode = TomlParserMode.Tolerant }), toml.Length);

        ParserResult strict;
        try
        {
            strict = ReadEvents(TomlParser.Create(toml, options), toml.Length);
        }
        catch (TomlException ex)
        {
            FuzzAssert.True(tolerant.HasErrors, $"The strict parser rejects a document that the tolerant parser accepts: {ex.Message}");
            return;
        }

        FuzzAssert.False(tolerant.HasErrors, $"The strict parser accepts a document that the tolerant parser rejects: {tolerant.Error}");
        FuzzAssert.Equal(tolerant.Events, strict.Events, "The strict and tolerant parsers produce different events");

        // The options only change what is decoded eagerly, not what the events say
        var defaults = ReadEvents(TomlParser.Create(toml, new TomlParserOptions()), toml.Length);
        FuzzAssert.Equal(defaults.Events, strict.Events, "The parser options change the events");

        using var reader = new StringReader(toml);
        var fromReader = ReadEvents(TomlParser.Create(reader, options), toml.Length);
        FuzzAssert.Equal(strict.Events, fromReader.Events, "The TextReader overload produces different events");
    }

    private static void Deserialize(ReadOnlySpan<byte> data)
    {
        TomlTable? fromStream;
        string? streamError = null;
        try
        {
            using var stream = new MemoryStream(data.ToArray());
            fromStream = TomlSerializer.Deserialize<TomlTable>(stream);
            FuzzAssert.True(fromStream is not null, "Deserialize returns null");
        }
        catch (TomlException ex)
        {
            fromStream = null;
            streamError = ex.Message;
        }

        string toml;
        try
        {
            toml = StrictUtf8.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            FuzzAssert.True(streamError is not null, "Deserialize accepts a stream that is not valid UTF-8");
            return;
        }

        TomlTable? fromString;
        try
        {
            fromString = TomlSerializer.Deserialize<TomlTable>(toml);
        }
        catch (TomlException ex)
        {
            FuzzAssert.True(streamError is not null, $"Deserialize rejects a string whose UTF-8 stream it accepts: {ex.Message}");
            FuzzAssert.False(TomlSerializer.TryDeserialize<TomlTable>(toml, out _), "TryDeserialize accepts a string that Deserialize rejects");
            return;
        }

        FuzzAssert.True(streamError is null, $"Deserialize accepts a string whose UTF-8 stream it rejects: {streamError}");
        FuzzAssert.True(fromString is not null && fromStream is not null, "Deserialize returns null");
        FuzzAssert.True(TomlSerializer.TryDeserialize<TomlTable>(toml, out _), "TryDeserialize rejects a string that Deserialize accepts");

        var expected = Canonicalize(fromString);
        FuzzAssert.Equal(expected, Canonicalize(fromStream), "The string and stream overloads give different models");

        var serialized = TomlSerializer.Serialize(fromString);
        TomlTable? reloaded;
        try
        {
            reloaded = TomlSerializer.Deserialize<TomlTable>(serialized);
        }
        catch (TomlException ex)
        {
            throw new FuzzAssertionException($"The serialized model cannot be deserialized: {FuzzAssert.Format(serialized)}", ex);
        }

        FuzzAssert.Equal(expected, Canonicalize(reloaded), $"The serialized model deserializes to a different model: {FuzzAssert.Format(serialized)}");
        FuzzAssert.Equal(serialized, TomlSerializer.Serialize(reloaded), "Serializing the reloaded model gives a different text");
    }

    /// <summary>A port of <c>StandardTests.CompareWithLanguageToml</c>: every pipeline agrees with the independent implementation.</summary>
    private static void Differential(ReadOnlySpan<byte> data)
    {
        var toml = FuzzText.Decode(data);
        var oracleDiagnostics = LanguageToml.TomlSyntaxTree.ParseText(toml, LanguageTomlOptions).GetDiagnostics();

        // The nesting limits differ (128 for the oracle, a model depth of 64 for this library), and both are by design
        if (oracleDiagnostics.Any(diagnostic => diagnostic.Id is "TOML0012"))
            return;

        var expected = oracleDiagnostics.Count == 0;
        var doc = SyntaxParser.Parse(toml);
        if (doc.Diagnostics.Any(diagnostic => IsLimitError(diagnostic.Message)))
            return;

        if (doc.HasErrors == expected)
        {
            // A few valid values do not fit in the .NET types, and both implementations do not handle them the same way
            if (expected && doc.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("cannot be represented", StringComparison.Ordinal)))
                return;

            FuzzAssert.Fail($"SyntaxParser: expected valid={expected}{Environment.NewLine}{FormatDiagnostics(doc.Diagnostics.Select(d => d.ToString()), oracleDiagnostics.Select(d => d.Id + " " + d.Message))}");
        }

        var tolerantParser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        while (tolerantParser.MoveNext())
        {
        }

        if (tolerantParser.Diagnostics.Any(diagnostic => IsLimitError(diagnostic.Message)))
            return;

        FuzzAssert.True(tolerantParser.HasErrors != expected, $"TomlParser (tolerant): expected valid={expected}{Environment.NewLine}{FormatDiagnostics(tolerantParser.Diagnostics.Select(d => d.ToString()), oracleDiagnostics.Select(d => d.Id + " " + d.Message))}");

        try
        {
            var strictParser = TomlParser.Create(toml);
            while (strictParser.MoveNext())
            {
            }

            FuzzAssert.True(expected, "TomlParser (strict): expected an error");
        }
        catch (TomlException ex) when (!IsLimitError(ex.Message))
        {
            FuzzAssert.False(expected, $"TomlParser (strict): unexpected error {ex.Message}");
        }

        try
        {
            _ = TomlSerializer.Deserialize<TomlTable>(toml);
            FuzzAssert.True(expected, "Deserialize: expected an error");
        }
        catch (TomlException ex) when (!IsLimitError(ex.Message))
        {
            FuzzAssert.False(expected, $"Deserialize: unexpected error {ex.Message}");
        }
    }

    private static bool IsLimitError(string message)
        => message.Contains("maximum depth of", StringComparison.Ordinal) || message.Contains("nested too deeply", StringComparison.Ordinal);

    private static string FormatDiagnostics(IEnumerable<string> library, IEnumerable<string> oracle)
        => $"Library: {string.Join(" | ", library)}{Environment.NewLine}Oracle: {string.Join(" | ", oracle)}";

    private static ParserResult ReadEvents(TomlParser parser, int length)
    {
        var events = new StringBuilder();
        var count = 0;
        while (parser.MoveNext())
        {
            count++;
            FuzzAssert.True(count <= (4 * length) + 16, "The parser does not make progress");

            var current = parser.Current;
            events.Append(current.Kind.ToString());
            if (current.Span is { } span)
            {
                events.Append(CultureInfo.InvariantCulture, $" [{span.Start.Offset}..{span.End.Offset}]");
            }

            // Strict mode throws the decoding errors, and tolerant mode reports them in its diagnostics
            try
            {
                switch (current.Kind)
                {
                    case TomlParseEventKind.PropertyName:
                        events.Append(' ').Append(FuzzText.Escape(parser.GetPropertyName()));
                        break;
                    case TomlParseEventKind.String:
                        events.Append(' ').Append(FuzzText.Escape(parser.GetString()));
                        break;
                    case TomlParseEventKind.Integer or TomlParseEventKind.Float or TomlParseEventKind.Boolean:
                        events.Append(CultureInfo.InvariantCulture, $" {current.Data}");
                        break;
                    case TomlParseEventKind.DateTime:
                        events.Append(' ').Append(current.StringValue);
                        break;
                }
            }
            catch (TomlException ex) when (parser.ParserOptions.Mode is TomlParserMode.Tolerant)
            {
                events.Append(" <error>");
                if (!parser.HasErrors)
                {
                    throw new FuzzAssertionException($"The tolerant parser cannot decode a value without reporting an error: {ex.Message}", ex);
                }
            }

            events.Append('\n');
        }

        var error = parser.HasErrors ? string.Join(" | ", parser.Diagnostics.Select(diagnostic => diagnostic.ToString())) : null;
        return new ParserResult(events.ToString(), parser.HasErrors, error);
    }

    private static string Canonicalize(object? value)
    {
        var builder = new StringBuilder();
        Canonicalize(value, builder);
        return builder.ToString();
    }

    private static void Canonicalize(object? value, StringBuilder builder)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case TomlTable table:
                builder.Append('{');
                foreach (var (key, item) in table.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    builder.Append(FuzzText.Escape(key)).Append(':');
                    Canonicalize(item, builder);
                    builder.Append(',');
                }

                builder.Append('}');
                break;
            case TomlTableArray tableArray:
                builder.Append("[[");
                foreach (var item in tableArray)
                {
                    Canonicalize(item, builder);
                    builder.Append(',');
                }

                builder.Append("]]");
                break;
            case TomlArray array:
                builder.Append('[');
                foreach (var item in array)
                {
                    Canonicalize(item, builder);
                    builder.Append(',');
                }

                builder.Append(']');
                break;
            case string text:
                builder.Append(FuzzText.Escape(text));
                break;
            case double number:
                builder.Append("float:").Append(number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case long number:
                builder.Append("integer:").Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                break;
            case TomlDateTime dateTime:
                builder.Append(CultureInfo.InvariantCulture, $"{dateTime.Kind}:{dateTime}");
                break;
            default:
                builder.Append(value.GetType().FullName).Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds(byte[] prefix)
    {
        using var document = FuzzResources.ReadJson(typeof(TomlFuzzTargets).Assembly, "toml-test.cases.json");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            if (item.TryGetProperty("toml", out var text))
            {
                yield return FuzzSeed.FromText(name, prefix, text.GetString()!);
            }
            else
            {
                // Not valid UTF-8: the raw bytes reach the stream overload as they are
                yield return new FuzzSeed(name, (byte[])[.. prefix, .. Convert.FromBase64String(item.GetProperty("tomlBase64").GetString()!)]);
            }
        }
    }

    private sealed record ParserResult(string Events, bool HasErrors, string? Error);
}
