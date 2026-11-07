using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Text;
using LanguageToml = Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.Toml.Fuzz;

/// <summary>
/// Malformed TOML must only throw <see cref="TomlException"/>. The syntax tree must round-trip any text, the strict and
/// tolerant event parsers must agree, a deserialized model must serialize to TOML that deserializes to the same model
/// whatever the serializer options, the overloads of the serializer and of <see cref="Serialization.TomlReader"/> must
/// agree, and every pipeline must agree with <c>Meziantou.Framework.Language.Toml</c>, an independent implementation, about
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
        new("toml-deserialize", Deserialize) { Seeds = () => GetSeeds(index => [(byte)(index * 73), (byte)(index * 29)]), Dictionary = Tokens },
        new("toml-reader", Reader) { Seeds = () => GetSeeds(index => [(byte)index]), Dictionary = Tokens },
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
        var decodingLexer = TomlLexer.Create(toml, new TomlLexerOptions { DecodeScalars = true });
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

            // Decoding the scalars only sets the values of the tokens
            FuzzAssert.True(decodingLexer.MoveNext(), $"The lexer that decodes the scalars ends before the token {token}");
            var decodedToken = decodingLexer.Current;
            FuzzAssert.Equal(FormatToken(token), FormatToken(decodedToken), "The lexer that decodes the scalars reads a different token");
            FuzzAssert.Equal(lexer.GetText(token), decodingLexer.GetText(decodedToken), $"The lexers give a different text for the token {token}");
        }

        FuzzAssert.False(decodingLexer.MoveNext(), "The lexer that decodes the scalars reads more tokens");
        FuzzAssert.Equal(string.Join(" | ", lexer.Errors), string.Join(" | ", decodingLexer.Errors), "The lexer that decodes the scalars reports different errors");
    }

    private static string FormatToken(TomlToken token)
        => FormattableString.Invariant($"{token.Kind} {token.Start.Offset}{token.Start}..{token.End.Offset}{token.End} {token.Data}");

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
        var options = ReadSerializerOptions(ref data);
        var bytes = data.ToArray();

        TomlTable? fromStream;
        string? streamError = null;
        try
        {
            using var stream = new MemoryStream(bytes);
            fromStream = TomlSerializer.Deserialize<TomlTable>(stream, options);
            FuzzAssert.True(fromStream is not null, "Deserialize returns null");
        }
        catch (TomlException ex)
        {
            fromStream = null;
            streamError = ex.Message;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var fromAsyncStream = TomlSerializer.DeserializeAsync<TomlTable>(stream, options).AsTask().GetAwaiter().GetResult();
            FuzzAssert.True(streamError is null, $"DeserializeAsync accepts a stream that Deserialize rejects: {streamError}");
            FuzzAssert.Equal(Canonicalize(fromStream), Canonicalize(fromAsyncStream), "DeserializeAsync and Deserialize give different models");
        }
        catch (TomlException ex)
        {
            FuzzAssert.True(streamError is not null, $"DeserializeAsync rejects a stream that Deserialize accepts: {ex.Message}");
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
            fromString = TomlSerializer.Deserialize<TomlTable>(toml, options);
        }
        catch (TomlException ex)
        {
            FuzzAssert.True(streamError is not null, $"Deserialize rejects a string whose UTF-8 stream it accepts: {ex.Message}");
            FuzzAssert.False(TomlSerializer.TryDeserialize<TomlTable>(toml, out _, options), "TryDeserialize accepts a string that Deserialize rejects");
            using var reader = new StringReader(toml);
            FuzzAssert.False(TomlSerializer.TryDeserialize<TomlTable>(reader, out _, options), "TryDeserialize (TextReader) accepts a string that Deserialize rejects");
            CheckDictionaryTarget(toml, options, expected: null);

            // Keeping the last value of a duplicate key only makes more documents valid
            if (options.DuplicateKeyHandling is not TomlDuplicateKeyHandling.Error)
            {
                FuzzAssert.False(TomlSerializer.TryDeserialize<TomlTable>(toml, out _), "DuplicateKeyHandling.LastWins rejects a document that DuplicateKeyHandling.Error accepts");
            }

            return;
        }

        FuzzAssert.True(streamError is null, $"Deserialize accepts a string whose UTF-8 stream it rejects: {streamError}");
        FuzzAssert.True(fromString is not null && fromStream is not null, "Deserialize returns null");
        FuzzAssert.True(TomlSerializer.TryDeserialize<TomlTable>(toml, out var tried, options), "TryDeserialize rejects a string that Deserialize accepts");

        var expected = Canonicalize(fromString);
        FuzzAssert.Equal(expected, Canonicalize(fromStream), "The string and stream overloads give different models");
        FuzzAssert.Equal(expected, Canonicalize(tried), "TryDeserialize and Deserialize give different models");
        using (var reader = new StringReader(toml))
        {
            FuzzAssert.True(TomlSerializer.TryDeserialize<TomlTable>(reader, out var triedFromReader, options), "TryDeserialize (TextReader) rejects a string that Deserialize accepts");
            FuzzAssert.Equal(expected, Canonicalize(triedFromReader), "TryDeserialize (TextReader) and Deserialize give different models");
        }

        CheckDictionaryTarget(toml, options, expected);
        if (options.DuplicateKeyHandling is not TomlDuplicateKeyHandling.Error && TomlSerializer.TryDeserialize<TomlTable>(toml, out var withoutDuplicates))
        {
            FuzzAssert.Equal(Canonicalize(withoutDuplicates), expected, "DuplicateKeyHandling.LastWins changes the model of a document without duplicate keys");
        }

        // DottedKeyHandling.Expand writes a key containing '.' as a dotted path, which is read back as nested tables
        var roundTrips = options.DottedKeyHandling is TomlDottedKeyHandling.Literal || !HasDottedKey(fromString);
        string serialized;
        try
        {
            serialized = TomlSerializer.Serialize(fromString, options);
        }
        catch (TomlException) when (!roundTrips)
        {
            return;
        }

        TomlTable? reloaded;
        try
        {
            // The serializer never writes a key twice
            reloaded = TomlSerializer.Deserialize<TomlTable>(serialized, options with { DuplicateKeyHandling = TomlDuplicateKeyHandling.Error });
        }
        catch (TomlException) when (!roundTrips)
        {
            return;
        }
        catch (TomlException ex)
        {
            throw new FuzzAssertionException($"The serialized model cannot be deserialized: {FuzzAssert.Format(serialized)}", ex);
        }

        if (!roundTrips)
            return;

        // TableArrayStyle.InlineArrayOfTables writes an array of tables as a static array of inline tables, which is read
        // back as a TomlArray: the TOML value is the same, but not the model. An array of tables that contains another one
        // is written with headers, until a round trip turns the inner one into a TomlArray, so the text is only stable
        // after a round trip per nesting level.
        if (options.TableArrayStyle is TomlTableArrayStyle.InlineArrayOfTables)
        {
            var value = Canonicalize(fromString, tableArraysAsArrays: true);
            for (var i = 0; ; i++)
            {
                FuzzAssert.Equal(value, Canonicalize(reloaded, tableArraysAsArrays: true), $"The serialized model deserializes to a different value: {FuzzAssert.Format(serialized)}");
                var next = TomlSerializer.Serialize(reloaded, options);
                if (next == serialized)
                    break;

                FuzzAssert.True(i < 70, $"Serializing the reloaded model does not converge: {FuzzAssert.Format(serialized)}");
                serialized = next;
                reloaded = TomlSerializer.Deserialize<TomlTable>(serialized, options);
            }

            return;
        }

        FuzzAssert.Equal(expected, Canonicalize(reloaded), $"The serialized model deserializes to a different model: {FuzzAssert.Format(serialized)}");
        FuzzAssert.Equal(serialized, TomlSerializer.Serialize(reloaded, options), "Serializing the reloaded model gives a different text");
    }

    private static TomlSerializerOptions ReadSerializerOptions(ref ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var styles = FuzzInput.ReadByte(ref data);
        return new TomlSerializerOptions
        {
            DuplicateKeyHandling = (flags & 1) != 0 ? TomlDuplicateKeyHandling.LastWins : TomlDuplicateKeyHandling.Error,
            DottedKeyHandling = (flags & 2) != 0 ? TomlDottedKeyHandling.Expand : TomlDottedKeyHandling.Literal,
            MappingOrder = (TomlMappingOrderPolicy)((flags >> 2) & 3),
            WriteIndented = (flags & 16) != 0,
            IndentSize = (flags & 32) != 0 ? 1 : 4,
            NewLine = (flags & 64) != 0 ? TomlNewLineKind.CrLf : TomlNewLineKind.Lf,
            TableArrayStyle = (flags & 128) != 0 ? TomlTableArrayStyle.InlineArrayOfTables : TomlTableArrayStyle.Headers,
            InlineTablePolicy = (TomlInlineTablePolicy)((styles & 3) % 3),
            StringStylePreferences = new TomlStringStylePreferences
            {
                DefaultStyle = (TomlStringStyle)((styles >> 2) & 3),
                PreferLiteralWhenNoEscapes = (styles & 16) != 0,
                AllowHexEscapes = (styles & 32) != 0,
            },
        };
    }

    private static bool HasDottedKey(object? value) => value switch
    {
        TomlTable table => table.Any(entry => entry.Key.Contains('.', StringComparison.Ordinal) || HasDottedKey(entry.Value)),
        TomlTableArray tableArray => tableArray.Any(HasDottedKey),
        TomlArray array => array.Any(HasDottedKey),
        _ => false,
    };

    private static void CheckDictionaryTarget(string toml, TomlSerializerOptions options, string? expected)
    {
        Dictionary<string, object>? dictionary;
        try
        {
            dictionary = TomlSerializer.Deserialize<Dictionary<string, object>>(toml, options);
        }
        catch (TomlException ex)
        {
            FuzzAssert.True(expected is null, $"Deserialize<Dictionary<string, object>> rejects a document that Deserialize<TomlTable> accepts: {ex.Message}");
            return;
        }

        FuzzAssert.True(expected is not null, "Deserialize<Dictionary<string, object>> accepts a document that Deserialize<TomlTable> rejects");
        FuzzAssert.Equal(expected, Canonicalize(dictionary), "Deserialize<Dictionary<string, object>> and Deserialize<TomlTable> give different models");
    }

    /// <summary>
    /// The <see cref="TomlReader"/> of a string and of a <see cref="TextReader"/> read the same tokens, the getters only throw
    /// <see cref="TomlException"/>, and <see cref="TomlReader.Skip"/> moves to the token after the end of the current value.
    /// </summary>
    private static void Reader(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var toml = FuzzText.Decode(data);
        var options = new TomlSerializerOptions { DuplicateKeyHandling = (flags & 1) != 0 ? TomlDuplicateKeyHandling.LastWins : TomlDuplicateKeyHandling.Error };

        var tokens = ReadTokens(TomlReader.Create(toml, options), toml.Length);
        using (var textReader = new StringReader(toml))
        {
            var tokensFromReader = ReadTokens(TomlReader.Create(textReader, options), toml.Length);
            FuzzAssert.Equal(string.Join('\n', tokens.Select(token => token.Text)), string.Join('\n', tokensFromReader.Select(token => token.Text)), "The TextReader overload reads different tokens");
        }

        if (tokens.Count == 0 || tokens[^1].Type is null)
            return;

        // Skip the values selected by the flags, and check that the reader lands on the token after their end
        var reader = TomlReader.Create(toml, options);
        var index = -1;
        var step = 0;
        while (true)
        {
            var skip = index >= 0 && (step++ % 4) == (flags >> 1) % 4;
            if (skip)
            {
                var end = GetValueEnd(tokens, index);
                reader.Skip();
                index = end + 1;
            }
            else
            {
                index++;
                if (!reader.Read())
                {
                    FuzzAssert.Equal(tokens.Count, index, "Read returns false before the last token");
                    break;
                }
            }

            if (index >= tokens.Count)
            {
                FuzzAssert.False(reader.Read(), "The reader reads more tokens than the first pass");
                break;
            }

            FuzzAssert.Equal(tokens[index].Type, reader.TokenType, $"The reader lands on a different token at {index}");
            FuzzAssert.Equal(tokens[index].Span, FormatSpan(reader.CurrentSpan), $"The reader lands on a token with a different span at {index}");
        }
    }

    private static int GetValueEnd(List<ReaderToken> tokens, int index)
    {
        if (tokens[index].Type is not (TomlTokenType.StartArray or TomlTokenType.StartTable))
            return index;

        var depth = 0;
        for (var i = index; i < tokens.Count; i++)
        {
            if (tokens[i].Type is TomlTokenType.StartArray or TomlTokenType.StartTable)
            {
                depth++;
            }
            else if (tokens[i].Type is TomlTokenType.EndArray or TomlTokenType.EndTable)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return tokens.Count - 1;
    }

    private static List<ReaderToken> ReadTokens(TomlReader reader, int length)
    {
        var tokens = new List<ReaderToken>();
        var depth = 0;
        while (true)
        {
            try
            {
                if (!reader.Read())
                    break;
            }
            catch (TomlException ex)
            {
                // A null type marks the error, which ends the tokens
                tokens.Add(new ReaderToken(null, null, "<error> " + ex.Message));
                break;
            }

            FuzzAssert.True(tokens.Count <= (4 * length) + 16, "The reader does not make progress");

            var type = reader.TokenType;
            depth += type switch
            {
                TomlTokenType.StartArray or TomlTokenType.StartTable => 1,
                TomlTokenType.EndArray or TomlTokenType.EndTable => -1,
                _ => 0,
            };
            FuzzAssert.True(depth >= 0, "The reader ends more containers than it starts");

            var span = FormatSpan(reader.CurrentSpan);
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"{type} {span} inline={reader.IsInlineContainer}");
            if (type is TomlTokenType.PropertyName)
            {
                var name = reader.PropertyName;
                FuzzAssert.True(name is not null, "The PropertyName token has no name");
                FuzzAssert.True(reader.PropertyNameEquals(name), "PropertyNameEquals returns false for the PropertyName");
                FuzzAssert.False(reader.PropertyNameEquals(name + "\0"), "PropertyNameEquals returns true for another name");
                FuzzAssert.True(reader.TryGetPropertyNameHash(out _), "TryGetPropertyNameHash returns false for a PropertyName token");
                text.Append(' ').Append(FuzzText.Escape(name));
            }
            else
            {
                FuzzAssert.True(reader.PropertyName is null, $"The {type} token has a property name");
                FuzzAssert.False(reader.TryGetPropertyNameHash(out _), $"TryGetPropertyNameHash returns true for a {type} token");
            }

            // The getters throw a TomlException for another token type. The document is valid once Read succeeds, so they
            // succeed for their token type, except GetDecimal for a float out of the range of decimal (inf, nan, 1e300).
            var isNumber = type is TomlTokenType.Integer or TomlTokenType.Float;
            AppendGetter(text, nameof(reader.GetString), type is TomlTokenType.String, () => FuzzText.Escape(reader.GetString()));
            AppendGetter(text, nameof(reader.GetInt64), type is TomlTokenType.Integer, () => reader.GetInt64().ToString(CultureInfo.InvariantCulture));
            AppendGetter(text, nameof(reader.GetDouble), isNumber, () => reader.GetDouble().ToString("R", CultureInfo.InvariantCulture));
            AppendGetter(text, nameof(reader.GetDecimal), type is TomlTokenType.Integer, () => reader.GetDecimal().ToString(CultureInfo.InvariantCulture), canFail: type is TomlTokenType.Float);
            AppendGetter(text, nameof(reader.GetBoolean), type is TomlTokenType.Boolean, () => reader.GetBoolean() ? "true" : "false");
            AppendGetter(text, nameof(reader.GetTomlDateTime), type is TomlTokenType.DateTime, () => reader.GetTomlDateTime().ToString());
            AppendGetter(text, nameof(reader.GetRawText), reader.CurrentSpan is not null, () => FuzzText.Escape(reader.GetRawText()));
            tokens.Add(new ReaderToken(type, span, text.ToString()));
        }

        return tokens;
    }

    private static void AppendGetter(StringBuilder text, string name, bool succeeds, Func<string> getter, bool canFail = false)
    {
        text.Append(" | ");
        try
        {
            text.Append(getter());
            FuzzAssert.True(succeeds || canFail, $"{name} succeeds for the token {text}");
        }
        catch (TomlException ex)
        {
            FuzzAssert.False(succeeds, $"{name} throws for the token {text}: {ex.Message}");
            text.Append("<error>");
        }
    }

    private static string? FormatSpan(TomlSourceSpan? span)
        => span is { } value ? FormattableString.Invariant($"{value.Start.Offset}{value.Start}..{value.End.Offset}{value.End}") : null;

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

    private static string Canonicalize(object? value, bool tableArraysAsArrays = false)
    {
        var builder = new StringBuilder();
        Canonicalize(value, builder, tableArraysAsArrays);
        return builder.ToString();
    }

    private static void Canonicalize(object? value, StringBuilder builder, bool tableArraysAsArrays)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case IDictionary<string, object> table:
                builder.Append('{');
                foreach (var (key, item) in table.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    builder.Append(FuzzText.Escape(key)).Append(':');
                    Canonicalize(item, builder, tableArraysAsArrays);
                    builder.Append(',');
                }

                builder.Append('}');
                break;
            case TomlTableArray tableArray:
                builder.Append(tableArraysAsArrays ? "[" : "[[");
                foreach (var item in tableArray)
                {
                    Canonicalize(item, builder, tableArraysAsArrays);
                    builder.Append(',');
                }

                builder.Append(tableArraysAsArrays ? "]" : "]]");
                break;
            case TomlArray array:
                builder.Append('[');
                foreach (var item in array)
                {
                    Canonicalize(item, builder, tableArraysAsArrays);
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

    private static IEnumerable<FuzzSeed> GetSeeds(byte[] prefix) => GetSeeds(_ => prefix);

    // The option bytes vary from a seed to the next, so that the seeds cover every option
    private static IEnumerable<FuzzSeed> GetSeeds(Func<int, byte[]> getPrefix)
    {
        using var document = FuzzResources.ReadJson(typeof(TomlFuzzTargets).Assembly, "toml-test.cases.json");
        var index = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var prefix = getPrefix(index++);
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

    private sealed record ReaderToken(TomlTokenType? Type, string? Span, string Text);
}
