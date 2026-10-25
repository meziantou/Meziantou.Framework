using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Css;
using Meziantou.Framework.Language.Ini;
using Meziantou.Framework.Language.Json;
using Meziantou.Framework.Language.Toml;
using CssFactory = Meziantou.Framework.Language.Css.SyntaxFactory;
using CssKind = Meziantou.Framework.Language.Css.SyntaxKind;
using IniFactory = Meziantou.Framework.Language.Ini.SyntaxFactory;
using JsonFactory = Meziantou.Framework.Language.Json.SyntaxFactory;
using TomlFactory = Meziantou.Framework.Language.Toml.SyntaxFactory;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// The token factories that escape a value into syntax: the text of the token, parsed where the token goes, gives no
/// diagnostics and a token with the same text and value, or the factory throws the exception it documents for a value it
/// cannot write. And <see cref="TomlFactory.ParseValue"/>, which reproduces its text and reports diagnostics exactly when
/// the text is not a value a key/value pair accepts.
/// </summary>
internal static class SyntaxFactoryFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "\"", "'", "\\", "\n", "\r\n", "\r", "\t", " ", "\0", "\u0001", "\u007F", "\u0085", "\u00A0", "\u2028", "\uFEFF", "\uD800", "\uDC00", "\uD83D\uDE00", "\uFFFD",
        "=", ":", ";", "#", "[", "]", "{", "}", ",", ".", "-", "--", "_", "0", "1", "-1", "a", "key", "C#", "a b", "\\u0041", "\\n", "/*",
        "[1, 2]", "{ a = 1 }", "'''", "\"\"\"", "inf", "nan", "1979-05-27T07:32:00Z", "07:32:00", "true", " # comment",
    ];

    private static readonly string[] Strings =
    [
        "", "value", "a b", " padded ", "quote\"s", "single'quote", "both\"'", "back\\slash", "tab\tand\nnew\r\nlines", "\0nul", "\u0001\u001F\u007F\u0080\u009F",
        "caf\u00E9", "\uD83D\uDE00", "\uD800", "x\uDC00y", "\uFEFFbom", "\u2028\u2029", "C#", "a;b", "a=b", "a:b", "[section]", "-", "--", "-1", "1x", "_", "url", "\uFFFD",
    ];

    private static readonly string[] TomlValues =
    [
        "1", "-0", "+1_000", "0x1F", "1.5e-3", "inf", "nan", "true", "\"basic\\n\"", "'literal'", "\"\"\"\nmulti\n\"\"\"", "'''\nraw\n'''",
        "1979-05-27T07:32:00.999Z", "1979-05-27 07:32:00", "1979-05-27", "07:32:00", "07:32", "[1, [2, 3], { a = 1 }]", "{ a = 1, b.c = [] }",
        "[\n  1, # one\n  2,\n]", "{ a = 1, a = 2 }", "1 # comment", "1\n# after\n", "\n1", "# first\n1", "", " ", "1 2", "1\nb = 2", "[1,", "{ a = 1, }", "\"\\e\\x41\"",
    ];

    private enum Mode
    {
        TomlString,
        TomlKeyPart,
        TomlInteger,
        TomlFloat,
        TomlOffsetDateTime,
        TomlLocalDateTime,
        TomlLocalDate,
        TomlLocalTime,
        TomlParseValue,
        JsonString,
        CssIdentifier,
        IniKey,
        IniSectionName,
        IniValue,
    }

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-syntax-factory", Run) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// Byte 0: the factory (<see cref="Mode"/>). Byte 1: the options (TOML: bit 0 reads TOML 1.0, the other bits the
    /// nesting limit of <see cref="TomlFactory.ParseValue"/>; INI: the options of the language-ini target). Then the
    /// value: a text, or the little-endian bytes of a number.
    /// </summary>
    private static void Run(ReadOnlySpan<byte> data)
    {
        var mode = (Mode)(FuzzInput.ReadByte(ref data) % (byte)(Mode.IniValue + 1));
        var flags = FuzzInput.ReadByte(ref data);
        var tomlOptions = new TomlParseOptions
        {
            Version = (flags & 1) == 0 ? TomlVersion.V1_1 : TomlVersion.V1_0,
            MaxDepth = LanguageFuzzInput.ReadDepth((byte)(flags >> 1)),
        };

        switch (mode)
        {
            case Mode.TomlString:
                TomlString(FuzzText.Decode(data), tomlOptions);
                break;
            case Mode.TomlKeyPart:
                TomlKeyPart(FuzzText.Decode(data), tomlOptions);
                break;
            case Mode.TomlInteger:
                var integer = ReadInt64(data);
                CheckTomlValue(TomlFactory.Literal(integer), integer, tomlOptions);
                break;
            case Mode.TomlFloat:
                var number = BitConverter.Int64BitsToDouble(ReadInt64(data));
                CheckTomlValue(TomlFactory.Literal(number), number, tomlOptions);
                break;
            case Mode.TomlOffsetDateTime:
                var dateTimeOffset = ReadDateTimeOffset(data);
                CheckTomlValue(TomlFactory.Literal(dateTimeOffset), dateTimeOffset, tomlOptions);
                break;
            case Mode.TomlLocalDateTime:
                var dateTime = new DateTime(ReadTicks(data, DateTime.MaxValue.Ticks), (DateTimeKind)((flags >> 1) % 3));
                CheckTomlValue(TomlFactory.Literal(dateTime), DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), tomlOptions);
                break;
            case Mode.TomlLocalDate:
                var date = DateOnly.FromDayNumber((int)ReadTicks(data, DateOnly.MaxValue.DayNumber));
                CheckTomlValue(TomlFactory.Literal(date), date, tomlOptions);
                break;
            case Mode.TomlLocalTime:
                var time = new TimeOnly(ReadTicks(data, TimeOnly.MaxValue.Ticks));
                CheckTomlValue(TomlFactory.Literal(time), time, tomlOptions);
                break;
            case Mode.TomlParseValue:
                TomlParseValue(FuzzText.Decode(data), tomlOptions);
                break;
            case Mode.JsonString:
                JsonString(FuzzText.Decode(data));
                break;
            case Mode.CssIdentifier:
                CssIdentifier(FuzzText.Decode(data));
                break;
            case Mode.IniKey or Mode.IniSectionName or Mode.IniValue:
                IniToken(mode, FuzzText.Decode(data), ReadIniOptions(flags));
                break;
        }
    }

    private static void TomlString(string value, TomlParseOptions options)
    {
        SyntaxToken token;
        try
        {
            token = TomlFactory.Literal(value);
        }
        catch (ArgumentException)
        {
            // Documented for a lone surrogate, which no TOML text can hold
            FuzzAssert.False(IsWellFormedUtf16(value), $"Literal({FuzzAssert.Format(value)}) throws for a string without a lone surrogate");
            return;
        }

        FuzzAssert.True(IsWellFormedUtf16(value), $"Literal({FuzzAssert.Format(value)}) accepts a lone surrogate");
        CheckTomlValue(token, value, options);
    }

    private static void TomlKeyPart(string name, TomlParseOptions options)
    {
        SyntaxToken token;
        try
        {
            token = TomlFactory.KeyPart(name);
        }
        catch (ArgumentException)
        {
            FuzzAssert.False(IsWellFormedUtf16(name), $"KeyPart({FuzzAssert.Format(name)}) throws for a name without a lone surrogate");
            return;
        }

        FuzzAssert.True(IsWellFormedUtf16(name), $"KeyPart({FuzzAssert.Format(name)}) accepts a lone surrogate");
        var text = token.Text + " = 1\n";
        var tree = TomlSyntaxTree.ParseText(text, options);
        var context = $"KeyPart({FuzzAssert.Format(name)}) in {FuzzAssert.Format(text)} ({options.Version})";
        AssertNoDiagnostics(tree.GetDiagnostics(), context);
        var key = tree.GetRoot().RootProperties.Single().Key;
        FuzzAssert.Equal(1, key.Parts.Count, $"{context} is not a key of one part");
        AssertSameToken(token, key.Parts[0], context);
        FuzzAssert.Equal(name, key.Names[0], $"{context} does not read back as the name");
    }

    /// <summary>The token, as the value of a key/value pair, reads back as the same token with the same value.</summary>
    private static void CheckTomlValue<TValue>(SyntaxToken token, TValue expected, TomlParseOptions options)
        where TValue : notnull
    {
        var text = "a = " + token.Text + "\n";
        var tree = TomlSyntaxTree.ParseText(text, options);
        var context = $"Literal({FuzzAssert.Format(expected)}) in {FuzzAssert.Format(text)} ({options.Version})";
        AssertNoDiagnostics(tree.GetDiagnostics(), context);
        var value = tree.GetRoot().RootProperties.Single().Value;
        FuzzAssert.Equal(token.Text, value.ToString(), $"{context} is not a value of one token");
        var parsed = value.GetFirstToken();
        AssertSameToken(token, parsed, context);
        FuzzAssert.True(SameValue(expected, token.Value), $"{context}: the token has the value {FuzzAssert.Format(token.Value)}");
        FuzzAssert.True(SameValue(expected, parsed.Value), $"{context} reads back as {FuzzAssert.Format(parsed.Value)}");
    }

    /// <summary>
    /// The value reproduces its text, and has diagnostics exactly when <c>a = </c> followed by the text is not a document
    /// of one key/value pair without diagnostics: the comments and line breaks before the value, which a key/value pair
    /// does not allow after its <c>=</c>, go before the key. A key defined twice in an inline table is reported by the
    /// document only (<see cref="TomlSyntaxTree.GetDiagnostics()"/>), not by its nodes.
    /// </summary>
    private static void TomlParseValue(string text, TomlParseOptions options)
    {
        var value = TomlFactory.ParseValue(text, options);
        FuzzAssert.Equal(text, value.ToFullString(), "ParseValue does not reproduce the text");
        FuzzAssert.True(value.Parent is null, "ParseValue returns a node with a parent");

        var leading = value.GetLeadingTrivia().ToFullString();
        FuzzAssert.True(text.StartsWith(leading, StringComparison.Ordinal), $"The leading trivia of the value of {FuzzAssert.Format(text)} does not start the text");
        var document = leading + "a = " + text[leading.Length..];
        var root = TomlSyntaxTree.ParseText(document, options).GetRoot();
        var isPair = !root.ContainsDiagnostics && root.Entries is [TomlPropertySyntax property] && property.Key.ToString() == "a";
        FuzzAssert.Equal(isPair, !value.ContainsDiagnostics, $"ParseValue({FuzzAssert.Format(text)}) and the document {FuzzAssert.Format(document)} ({options.Version}, depth {options.MaxDepth}) disagree about the value being valid");
    }

    private static void JsonString(string value)
    {
        var token = JsonFactory.Literal(value);
        var tree = JsonSyntaxTree.ParseText(token.Text);
        var context = $"Literal({FuzzAssert.Format(value)}) written {FuzzAssert.Format(token.Text)}";
        AssertNoDiagnostics(tree.GetDiagnostics(), context);
        var root = tree.GetRoot();
        FuzzAssert.True(root.Values is [JsonStringSyntax], $"{context} is not a document of one string");
        var parsed = (JsonStringSyntax)root.Values[0];
        AssertSameToken(token, parsed.StringToken, context);
        FuzzAssert.Equal(value, parsed.Value, $"{context} does not read back as the value");
    }

    private static void CssIdentifier(string value)
    {
        SyntaxToken token;
        try
        {
            token = CssFactory.Identifier(value);
        }
        catch (ArgumentException)
        {
            // Documented for an empty value
            FuzzAssert.Equal(0, value.Length, $"Identifier({FuzzAssert.Format(value)}) throws");
            return;
        }

        // The identifier is the value of a declaration, where nothing but an identifier can start with what it holds
        var text = "x:" + token.Text;
        var tree = CssSyntaxTree.ParseText(text, new CssParseOptions { SourceKind = CssSourceKind.DeclarationList });
        var context = $"Identifier({FuzzAssert.Format(value)}) in {FuzzAssert.Format(text)}";
        AssertNoDiagnostics(tree.GetDiagnostics(), context);
        var tokens = tree.GetRoot().DescendantTokens().Where(item => !item.IsKind(CssKind.EndOfFileToken)).ToList();
        FuzzAssert.True(tokens is [_, _, var parsed] && parsed.IsKind(CssKind.IdentToken), $"{context} is not a declaration whose value is one identifier: {string.Join(", ", tokens.Select(item => (CssKind)item.RawKind))}");
        AssertSameToken(token, tokens[2], context);

        // CSS reads U+0000 and lone surrogates as U+FFFD (CSS Syntax Level 3, section 3.3), escaped or not
        var expected = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                expected.Append(value, i, 2);
                i++;
            }
            else
            {
                expected.Append(value[i] is '\0' || char.IsSurrogate(value[i]) ? '\uFFFD' : value[i]);
            }
        }

        FuzzAssert.Equal(expected.ToString(), tokens[2].ValueText, $"{context} does not read back as the value");
    }

    private static void IniToken(Mode mode, string value, IniParseOptions options)
    {
        SyntaxToken token;
        try
        {
            token = mode switch
            {
                Mode.IniKey => IniFactory.Key(value, options),
                Mode.IniSectionName => IniFactory.SectionName(value, options),
                _ => IniFactory.Value(value, options),
            };
        }
        catch (ArgumentException)
        {
            // Documented for a value that cannot be written so that the options read it back. A value of letters and
            // digits can always be written.
            FuzzAssert.False(value.Length > 0 && value.All(char.IsAsciiLetterOrDigit), $"{mode}({FuzzAssert.Format(value)}) throws");
            return;
        }

        var text = mode switch
        {
            Mode.IniKey => token.Text + "=v\n",
            Mode.IniSectionName => "[" + token.Text + "]\n",
            _ => "k=" + token.Text + "\n",
        };
        var tree = IniSyntaxTree.ParseText(text, options);
        var context = $"{mode}({FuzzAssert.Format(value)}) in {FuzzAssert.Format(text)}";
        AssertNoDiagnostics(tree.GetDiagnostics(), context);
        var root = tree.GetRoot();
        switch (mode)
        {
            case Mode.IniSectionName:
                FuzzAssert.True(root.Entries is [IniSectionSyntax], $"{context} is not one section");
                var section = (IniSectionSyntax)root.Entries[0];
                AssertSameToken(token, section.NameToken, context);
                FuzzAssert.Equal(value, section.Name, $"{context} does not read back as the name");
                break;

            default:
                FuzzAssert.True(root.Entries is [IniPropertySyntax], $"{context} is not one property");
                var property = (IniPropertySyntax)root.Entries[0];
                AssertSameToken(token, mode is Mode.IniKey ? property.KeyToken : property.ValueToken, context);
                FuzzAssert.Equal(value, mode is Mode.IniKey ? property.Key : property.Value, $"{context} does not read back as the value");
                FuzzAssert.Equal(0, property.ContinuationTokens.Count, $"{context} has continuation lines");
                break;
        }
    }

    private static void AssertNoDiagnostics(IEnumerable<Diagnostic> diagnostics, string context)
    {
        var list = diagnostics.ToList();
        FuzzAssert.True(list.Count == 0, $"{context} has diagnostics: {string.Join(", ", list.Select(diagnostic => $"{diagnostic.Id} at {diagnostic.Location.SourceSpan}: {diagnostic.Message}"))}");
    }

    private static void AssertSameToken(SyntaxToken expected, SyntaxToken actual, string context)
    {
        FuzzAssert.Equal(expected.RawKind, actual.RawKind, $"{context}: the parsed token has another kind");
        FuzzAssert.Equal(expected.Text, actual.Text, $"{context}: the parsed token has another text");
        FuzzAssert.Equal(expected.ValueText, actual.ValueText, $"{context}: the parsed token has another value text");
    }

    private static bool SameValue(object expected, object? actual) => (expected, actual) switch
    {
        // NaN reads back as NaN, whatever its payload
        (double left, double right) => double.IsNaN(left) ? double.IsNaN(right) : BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right),
        (DateTimeOffset left, DateTimeOffset right) => left.EqualsExact(right),
        (DateTime left, DateTime right) => left.Ticks == right.Ticks && right.Kind is DateTimeKind.Unspecified,
        _ => expected.Equals(actual),
    };

    private static bool IsWellFormedUtf16(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static IniParseOptions ReadIniOptions(byte flags) => new()
    {
        InlineComments = (IniInlineCommentMode)((flags & 3) % 3),
        AllowKeysWithoutValue = (flags & 4) != 0,
        AllowMultilineValues = (flags & 8) != 0,
        AllowQuotedValues = (flags & 16) == 0,
        ReportDuplicates = (flags & 32) != 0,
        NameComparer = (flags & 64) == 0 ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal,
    };

    private static long ReadInt64(ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[8];
        data[..Math.Min(8, data.Length)].CopyTo(buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    /// <summary>Reads a number from 0 to <paramref name="max"/>.</summary>
    private static long ReadTicks(ReadOnlySpan<byte> data, long max) => (long)((ulong)ReadInt64(data) % ((ulong)max + 1));

    /// <summary>Reads the ticks of the date and time from bytes 0-7, and the offset in minutes from bytes 8-9.</summary>
    private static DateTimeOffset ReadDateTimeOffset(ReadOnlySpan<byte> data)
    {
        var ticks = ReadTicks(data, DateTime.MaxValue.Ticks);
        var minutes = data.Length >= 10 ? BinaryPrimitives.ReadInt16LittleEndian(data[8..]) % 841 : 0;

        // The date and time in UTC has to be a DateTime too
        var utcTicks = ticks - (minutes * TimeSpan.TicksPerMinute);
        if (utcTicks < 0 || utcTicks > DateTime.MaxValue.Ticks)
        {
            minutes = 0;
        }

        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(minutes));
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        Mode[] textModes = [Mode.TomlString, Mode.TomlKeyPart, Mode.JsonString, Mode.CssIdentifier, Mode.IniKey, Mode.IniSectionName, Mode.IniValue];
        foreach (var mode in textModes)
        {
            for (var i = 0; i < Strings.Length; i++)
            {
                // TOML 1.0 and 1.1, and INI options that change what needs quotes or escapes
                var flags = (byte)(mode is Mode.IniKey or Mode.IniSectionName or Mode.IniValue ? i % 32 : i % 2);
                yield return FuzzSeed.FromText(FormattableString.Invariant($"{mode}-{i}"), [(byte)mode, flags], Strings[i]);
            }
        }

        long[] integers = [0, 1, -1, long.MaxValue, long.MinValue, 1_000_000];
        foreach (var value in integers)
        {
            yield return NumberSeed(Mode.TomlInteger, 0, value);
        }

        double[] floats = [0, -0.0, 1, -1.5, 0.1, 1e300, double.Epsilon, double.MaxValue, double.MinValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity, Math.ScaleB(1, -25), Math.ScaleB(1, -958), 6.626e-34, 1e21, 123456789012345680000.0];
        foreach (var value in floats)
        {
            yield return NumberSeed(Mode.TomlFloat, 1, BitConverter.DoubleToInt64Bits(value));
        }

        var dateTime = new DateTime(1979, 5, 27, 7, 32, 0, DateTimeKind.Utc).AddTicks(9_999_999);
        yield return NumberSeed(Mode.TomlLocalDateTime, 0, dateTime.Ticks);
        yield return NumberSeed(Mode.TomlLocalDateTime, 1, DateTime.MaxValue.Ticks);
        yield return NumberSeed(Mode.TomlLocalDate, 0, new DateOnly(1979, 5, 27).DayNumber);
        yield return NumberSeed(Mode.TomlLocalDate, 0, DateOnly.MaxValue.DayNumber);
        yield return NumberSeed(Mode.TomlLocalTime, 0, new TimeOnly(7, 32).Ticks);
        yield return NumberSeed(Mode.TomlLocalTime, 1, TimeOnly.MaxValue.Ticks);
        yield return new FuzzSeed("TomlOffsetDateTime-utc", (byte[])[(byte)Mode.TomlOffsetDateTime, 0, .. BitConverter.GetBytes(dateTime.Ticks), 0, 0]);
        yield return new FuzzSeed("TomlOffsetDateTime-offset", (byte[])[(byte)Mode.TomlOffsetDateTime, 1, .. BitConverter.GetBytes(dateTime.AddTicks(1).Ticks), .. BitConverter.GetBytes((short)-420)]);

        for (var i = 0; i < TomlValues.Length; i++)
        {
            yield return FuzzSeed.FromText(FormattableString.Invariant($"TomlParseValue-{i}"), [(byte)Mode.TomlParseValue, (byte)(i % 2)], TomlValues[i]);
        }

        // The values of the conformance suite
        using var document = FuzzResources.ReadJson(typeof(SyntaxFactoryFuzzTargets).Assembly, "toml-test.cases.json");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("toml", out var toml))
                continue;

            var name = item.GetProperty("name").GetString()!;
            var flags = item.GetProperty("versions").EnumerateArray().Any(version => version.GetString() == "1.1.0") ? (byte)0 : (byte)1;
            var properties = TomlSyntaxTree.ParseText(toml.GetString()!).GetRoot().DescendantNodes().OfType<TomlPropertySyntax>().Take(2).ToList();
            for (var i = 0; i < properties.Count; i++)
            {
                yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"TomlParseValue-{name}-{i}"), [(byte)Mode.TomlParseValue, flags], properties[i].Value.ToFullString());
            }
        }

        static FuzzSeed NumberSeed(Mode mode, byte flags, long value)
            => new(string.Create(CultureInfo.InvariantCulture, $"{mode}-{value}"), (byte[])[(byte)mode, flags, .. BitConverter.GetBytes(value)]);
    }
}
