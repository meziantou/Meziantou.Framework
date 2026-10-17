using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The CSS Syntax tests of https://github.com/SimonSapin/css-parsing-tests.</summary>
public sealed class CssParsingTests
{
    public static TheoryData<string, int> GetTests(string file)
    {
        var data = new TheoryData<string, int>();
        var tests = Load(file);
        for (var i = 0; i < tests.GetArrayLength() / 2; i++)
        {
            data.Add(file, i);
        }

        return data;
    }

    public static TheoryData<string, int> ComponentValueListTests => GetTests("component_value_list.json");

    public static TheoryData<string, int> OneComponentValueTests => GetTests("one_component_value.json");

    public static TheoryData<string, int> BlockContentsTests => GetTests("blocks_contents.json");

    public static TheoryData<string, int> OneDeclarationTests => GetTests("one_declaration.json");

    public static TheoryData<string, int> OneRuleTests => GetTests("one_rule.json");

    public static TheoryData<string, int> StylesheetTests => GetTests("stylesheet.json");

    public static TheoryData<string, int> RuleListTests => GetTests("rule_list.json");

    public static TheoryData<string, int> DeclarationListTests => GetTests("declaration_list.json");

    public static TheoryData<string, int> StylesheetBytesTests => GetTests("stylesheet_bytes.json");

    public static TheoryData<string, int> AnPlusBTests => GetTests("An+B.json");

    public static TheoryData<string, int> ColorTests
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var path in Directory.EnumerateFiles(TestPaths.External("css-parsing-tests"), "color_*.json").Order(StringComparer.Ordinal))
            {
                var file = Path.GetFileName(path);
                for (var i = 0; i < Load(file).GetArrayLength() / 2; i++)
                {
                    data.Add(file, i);
                }
            }

            return data;
        }
    }

    private static JsonElement Load(string file) => TestJson.Load(TestPaths.External("css-parsing-tests", file));

    // The tests that predate changes of CSS Syntax Level 3
    private static readonly Dictionary<string, string> OutdatedFiles = new(StringComparer.Ordinal)
    {
        ["component_value_list.json"] = "Outdated: the tokenizer no longer produces unicode-range and match tokens, and the non-ASCII ident code points changed",
        ["one_declaration.json"] = "Outdated: parse a declaration now stops at a semicolon (the remaining input is a syntax error) and removes the leading and trailing whitespace of the value",
        ["color_functions_5.json"] = "The expected value of light-dark() is a pair of colors, a representation that no specification defines",
    };

    private static readonly HashSet<(string File, int Index)> OutdatedTests =
    [
        ("component_value_list.json", 6),
        .. Enumerable.Range(38, 11).Select(i => ("component_value_list.json", i)),
        ("one_declaration.json", 11), ("one_declaration.json", 12),
        .. Enumerable.Range(14, 6).Select(i => ("one_declaration.json", i)),
        .. Enumerable.Range(16, 4).Select(i => ("color_functions_5.json", i)),
    ];

    private static (string Input, JsonNode? Expected) GetTest(string file, int index)
    {
        var (input, expected) = GetRawTest(file, index);
        return ((string)input!, expected);
    }

    private static (JsonNode? Input, JsonNode? Expected) GetRawTest(string file, int index)
    {
        Assert.SkipWhen(OutdatedTests.Contains((file, index)), OutdatedFiles.GetValueOrDefault(file, "Outdated"));
        var tests = Load(file);
        return (TestJson.ToNode(tests[index * 2]), TestJson.ToNode(tests[(index * 2) + 1]));
    }

    [Theory]
    [MemberData(nameof(ComponentValueListTests))]
    public void ComponentValueList(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        var preprocessed = CssTokenizer.Preprocess(input);
        AssertJson(expected, ToJson(CssParser.ParseListOfComponentValues(input), preprocessed), input);
    }

    [Theory]
    [MemberData(nameof(OneComponentValueTests))]
    public void OneComponentValue(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        var preprocessed = CssTokenizer.Preprocess(input);
        var value = CssParser.ParseComponentValue(input, out var error);
        AssertJson(expected, value is null ? new JsonArray("error", error) : ToJson(value, preprocessed), input);
    }

    [Theory]
    [MemberData(nameof(BlockContentsTests))]
    public void BlockContents(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        AssertJson(expected, ToJson(CssParser.ParseBlockContents(input, reportErrors: true), CssTokenizer.Preprocess(input)), input);
    }

    [Theory]
    [MemberData(nameof(OneDeclarationTests))]
    public void OneDeclaration(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        AssertJson(expected, ToJson(CssParser.ParseDeclaration(input), CssTokenizer.Preprocess(input)), input);
    }

    // https://drafts.csswg.org/css-syntax-3/#parse-a-declaration: the input after the declaration is a syntax error
    [Theory]
    [InlineData("foo:;", "extra-input")]
    [InlineData("foo:;bar:;", "extra-input")]
    [InlineData("foo: 1 ; ", "extra-input")]
    [InlineData("1px", "invalid")]
    public void OneDeclaration_Error(string input, string expected)
    {
        var error = Assert.IsType<CssSyntaxError>(CssParser.ParseDeclaration(input));
        Assert.Equal(expected, error.Kind);
    }

    [Theory]
    [MemberData(nameof(OneRuleTests))]
    public void OneRule(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        AssertJson(expected, ToJson(CssParser.ParseRule(input), CssTokenizer.Preprocess(input)), input);
    }

    [Theory]
    [MemberData(nameof(StylesheetTests))]
    public void Stylesheet(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        AssertJson(expected, ToJson(CssParser.ParseStylesheet(input, reportErrors: true), CssTokenizer.Preprocess(input)), input);
    }

    [Theory]
    [MemberData(nameof(RuleListTests))]
    public void RuleList(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        var stream = new CssComponentValueStream(CssParser.ParseListOfComponentValues(input));
        AssertJson(expected, ToJson(CssParser.ConsumeStylesheetContents(stream, reportErrors: true, topLevel: false), CssTokenizer.Preprocess(input)), input);
    }

    [Theory]
    [MemberData(nameof(DeclarationListTests))]
    public void DeclarationList(string file, int index)
    {
        var (input, expected) = GetTest(file, index);

        // Parse a list of declarations discards the at-rules (they were returned by the 2021 draft the tests follow)
        var expectedDeclarations = new JsonArray([.. expected!.AsArray().Where(node => (string?)node![0] != "at-rule").Select(node => node!.DeepClone())]);
        var declarations = CssParser.ParseDeclarationList(input, out var preprocessed, reportErrors: true);
        AssertJson(expectedDeclarations, ToJson(declarations, preprocessed), input);
    }

    [Theory]
    [MemberData(nameof(StylesheetBytesTests))]
    public void StylesheetBytes(string file, int index)
    {
        var (test, expected) = GetRawTest(file, index);
        var bytes = System.Text.Encoding.Latin1.GetBytes((string)test!["css_bytes"]!);
        var protocolEncoding = (string?)test["protocol_encoding"];
        var environmentEncoding = (string?)test["environment_encoding"] is { } label && WebEncodingRegistry.TryGetEncoding(label, out var environment) ? environment : (WebEncodingId?)null;
        var text = CssImportFetching.Decode(bytes, protocolEncoding, environmentEncoding, out var encoding);
        var actual = new JsonArray(ToJson(CssParser.ParseStylesheet(text, reportErrors: true), CssTokenizer.Preprocess(text)), WebEncodingRegistry.GetName(encoding).ToLowerInvariant());
        AssertJson(expected, actual, (string)test["css_bytes"]!);
    }

    [Theory]
    [MemberData(nameof(AnPlusBTests))]
    public void AnPlusB(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        var result = SelectorParser.ParseAnB(CssParser.ParseListOfComponentValues(input));
        AssertJson(expected, result is { } anb ? new JsonArray((double)anb.A, (double)anb.B) : null, input);
    }

    [Theory]
    [MemberData(nameof(ColorTests))]
    public void Color(string file, int index)
    {
        var (input, expected) = GetTest(file, index);
        var value = CssParser.ParseComponentValue(input, out _);
        var color = value is null ? null : CssColor.Parse(value);
        if (color is { LegacyRgb: false, Function: "hsl" or "hwb" } && expected is not null && ((string)expected!).StartsWith("rgb", StringComparison.Ordinal))
        {
            // hsl() and hwb() with a none component keep their color space (WPT color-valid-hsl and color-valid-hwb); the test
            // expects the color whose none components are 0, as in the 2021 draft
            Assert.StartsWith(color.Function + "(", color.Serialize(), StringComparison.Ordinal);
            input = input.Replace("none", "0", StringComparison.OrdinalIgnoreCase);
            color = CssColor.Parse(CssParser.ParseComponentValue(input, out _)!);
        }

        AssertJson(expected is null ? null : JsonValue.Create(UpdateColorExpectation((string)expected!)), color is null ? null : JsonValue.Create(color.Serialize()), input);
    }

    /// <summary>
    /// Updates the expected serialization of the color tests to the current CSS Color 4 specification (the tests follow the 2021 draft):
    /// <list type="bullet">
    /// <item>The precision of the sRGB channels and of the alpha is not defined (https://drafts.csswg.org/css-color-4/#serializing-sRGB-values):
    /// the tests keep all the digits, the implementation rounds the channels to integers and the alpha to 3 decimals like the browsers (WPT color-computed-hsl).</item>
    /// <item>The lightness of lab(), lch(), oklab() and oklch() and the chroma of lch() and oklch() are clamped at parsed-value time
    /// (https://drafts.csswg.org/css-color-4/#specifying-lab-lch, https://drafts.csswg.org/css-color-4/#specifying-oklab-oklch).</item>
    /// </list>
    /// </summary>
    private static string UpdateColorExpectation(string expected)
    {
        var open = expected.IndexOf('(', StringComparison.Ordinal);
        var name = expected[..open];
        var arguments = expected[(open + 1)..^1];
        if (name is "rgb" or "rgba")
        {
            var channels = arguments.Split(", ").Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var result = string.Join(", ", channels.Take(3).Select(v => Math.Round(v, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)));
            if (channels.Length == 4)
            {
                result += ", " + Math.Round(channels[3], 3, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
            }

            return name + "(" + result + ")";
        }

        if (name is "lab" or "lch" or "oklab" or "oklch")
        {
            var parts = arguments.Split(' ');
            var maximum = name is "lab" or "lch" ? 100 : 1;
            parts[0] = Math.Clamp(double.Parse(parts[0], CultureInfo.InvariantCulture), 0, maximum).ToString(CultureInfo.InvariantCulture);
            if (name is "lch" or "oklch")
            {
                parts[1] = Math.Max(double.Parse(parts[1], CultureInfo.InvariantCulture), 0).ToString(CultureInfo.InvariantCulture);
            }

            return name + "(" + string.Join(' ', parts) + ")";
        }

        return expected;
    }

    // The non-ASCII ident code points (https://drafts.csswg.org/css-syntax-3/#non-ascii-ident-code-point). WPT
    // css/css-syntax/non-ascii-codepoints.html stops at [0x203f, 0x2040] because of a missing comma in the test.
    [Theory]
    [InlineData(0xB7, 0xB7)]
    [InlineData(0xC0, 0xD6)]
    [InlineData(0xD8, 0xF6)]
    [InlineData(0xF8, 0x37D)]
    [InlineData(0x37F, 0x1FFF)]
    [InlineData(0x200C, 0x200D)]
    [InlineData(0x203F, 0x2040)]
    [InlineData(0x2070, 0x218F)]
    [InlineData(0x2C00, 0x2FEF)]
    [InlineData(0x3001, 0xD7FF)]
    [InlineData(0xF900, 0xFDCF)]
    [InlineData(0xFDF0, 0xFFFD)]
    [InlineData(0x10000, 0x10FFFF)]
    public void NonAsciiIdentCodePoints(int first, int last)
    {
        foreach (var codePoint in (int[])[first, (first + last) / 2, last])
        {
            Assert.Equal(CssTokenType.Ident, Assert.Single(CssTokenizer.Tokenize(char.ConvertFromUtf32(codePoint))).Type);
        }

        foreach (var codePoint in (int[])[first - 1, last + 1])
        {
            if (codePoint is < 0x80 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
                continue;

            var tokens = CssTokenizer.Tokenize(char.ConvertFromUtf32(codePoint));
            Assert.Equal(CssTokenType.Delim, Assert.Single(tokens).Type);
        }
    }

    private static void AssertJson(JsonNode? expected, JsonNode? actual, string input)
    {
        if (!JsonEquals(expected, actual))
        {
            Assert.Fail($"Input: {JsonSerializer.Serialize(input)}\nExpected: {expected?.ToJsonString()}\nActual:   {actual?.ToJsonString()}");
        }
    }

    // Numbers are compared with a tolerance
    private static bool JsonEquals(JsonNode? expected, JsonNode? actual)
    {
        if (expected is null || actual is null)
            return expected is null && actual is null;

        if (expected is JsonArray expectedArray)
        {
            if (actual is not JsonArray actualArray || expectedArray.Count != actualArray.Count)
                return false;

            for (var i = 0; i < expectedArray.Count; i++)
            {
                if (!JsonEquals(expectedArray[i], actualArray[i]))
                    return false;
            }

            return true;
        }

        if (expected is JsonValue expectedValue && expectedValue.GetValueKind() == JsonValueKind.Number)
        {
            if (actual is not JsonValue actualValue || actualValue.GetValueKind() != JsonValueKind.Number)
                return false;

            var a = expectedValue.GetValue<double>();
            var b = actualValue.GetValue<double>();
            return a == b || Math.Abs(a - b) <= 1e-6 * Math.Max(Math.Abs(a), Math.Abs(b));
        }

        return JsonNode.DeepEquals(expected, actual);
    }

    private static JsonArray ToJson(IEnumerable<CssSyntaxNode> nodes, string source) => new JsonArray([.. nodes.Select(n => ToJson(n, source))]);

    private static JsonArray ToJson(CssSyntaxNode node, string source) => node switch
    {
        CssSyntaxError error => new JsonArray("error", error.Kind),
        CssAtRule atRule => new JsonArray("at-rule", atRule.Name, ToJson(atRule.Prelude, source), atRule.Block is null ? null : ToJson(atRule.Block.Value, source)),
        CssQualifiedRule rule => new JsonArray("qualified rule", ToJson(rule.Prelude, source), ToJson(rule.Block.Value, source)),
        CssDeclaration declaration => new JsonArray("declaration", declaration.Name, ToJson(declaration.Value, source), declaration.Important),
        _ => throw new InvalidOperationException(),
    };

    private static JsonArray ToJson(List<CssComponentValue> values, string source)
    {
        var result = new JsonArray();
        foreach (var value in values)
        {
            result.Add(ToJson(value, source));
            AddEofError(result, value, source);
        }

        return result;
    }

    // The test suite reports the parse errors of strings and URLs ended by the end of the input
    private static void AddEofError(JsonArray result, CssComponentValue value, string source)
    {
        if (value is not CssPreservedToken { Token: var token } || token.End != source.Length)
            return;

        var text = source[token.Start..token.End];
        if (token.Type == CssTokenType.String && (text.Length < 2 || text[^1] != text[0] || text.EndsWith("\\" + text[0], StringComparison.Ordinal) && !text.EndsWith("\\\\" + text[0], StringComparison.Ordinal)))
        {
            result.Add(new JsonArray("error", "eof-in-string"));
        }
        else if (token.Type == CssTokenType.Url && !text.EndsWith(')', StringComparison.Ordinal))
        {
            result.Add(new JsonArray("error", "eof-in-url"));
        }
    }

    private static JsonNode ToJson(CssComponentValue value, string source)
    {
        switch (value)
        {
            case CssSimpleBlock block:
            {
                var result = new JsonArray(block.AssociatedToken switch
                {
                    CssTokenType.LeftCurlyBracket => "{}",
                    CssTokenType.LeftSquareBracket => "[]",
                    _ => "()",
                });
                foreach (var item in block.Value)
                {
                    result.Add(ToJson(item, source));
                    AddEofError(result, item, source);
                }

                return result;
            }

            case CssFunction function:
            {
                var result = new JsonArray("function", function.Name);
                foreach (var item in function.Value)
                {
                    result.Add(ToJson(item, source));
                    AddEofError(result, item, source);
                }

                return result;
            }

            case CssPreservedToken { Token: var token }:
                return token.Type switch
                {
                    CssTokenType.Ident => new JsonArray("ident", token.Value),
                    CssTokenType.AtKeyword => new JsonArray("at-keyword", token.Value),
                    CssTokenType.Hash => new JsonArray("hash", token.Value, token.IsIdHash ? "id" : "unrestricted"),
                    CssTokenType.String => new JsonArray("string", token.Value),
                    CssTokenType.BadString => new JsonArray("error", "bad-string"),
                    CssTokenType.Url => new JsonArray("url", token.Value),
                    CssTokenType.BadUrl => new JsonArray("error", "bad-url"),
                    CssTokenType.Delim => JsonValue.Create(token.Value),
                    CssTokenType.Number => new JsonArray("number", Representation(token, source), token.NumericValue, token.IsInteger ? "integer" : "number"),
                    CssTokenType.Percentage => new JsonArray("percentage", Representation(token, source, suffixLength: 1), token.NumericValue, IsIntegerRepresentation(Representation(token, source, suffixLength: 1)) ? "integer" : "number"),
                    CssTokenType.Dimension => new JsonArray("dimension", NumberRepresentation(token, source), token.NumericValue, token.IsInteger ? "integer" : "number", token.Value),
                    CssTokenType.UnicodeRange => new JsonArray("unicode-range", token.RangeStart, token.RangeEnd),
                    CssTokenType.Whitespace => JsonValue.Create(" "),
                    CssTokenType.Cdo => JsonValue.Create("<!--"),
                    CssTokenType.Cdc => JsonValue.Create("-->"),
                    CssTokenType.Colon => JsonValue.Create(":"),
                    CssTokenType.Semicolon => JsonValue.Create(";"),
                    CssTokenType.Comma => JsonValue.Create(","),
                    CssTokenType.RightCurlyBracket => new JsonArray("error", "}"),
                    CssTokenType.RightSquareBracket => new JsonArray("error", "]"),
                    CssTokenType.RightParenthesis => new JsonArray("error", ")"),
                    _ => throw new InvalidOperationException($"Unexpected token {token.Type}"),
                };

            default:
                throw new InvalidOperationException();
        }
    }

    private static string Representation(CssToken token, string source, int suffixLength = 0) => source[token.Start..(token.End - suffixLength)];

    // The percentage tokens of the current specification have no type flag: the test suite uses the representation
    private static bool IsIntegerRepresentation(string representation) => !representation.Contains('.', StringComparison.Ordinal) && !representation.Contains('e', StringComparison.OrdinalIgnoreCase);

    // The representation of the number of a dimension token: the numeric part of the source
    private static string NumberRepresentation(CssToken token, string source)
    {
        var text = source[token.Start..token.End];
        var position = 0;
        if (position < text.Length && text[position] is '+' or '-')
        {
            position++;
        }

        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            position++;
        }

        if (position + 1 < text.Length && text[position] == '.' && char.IsAsciiDigit(text[position + 1]))
        {
            position++;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        if (position + 1 < text.Length && text[position] is 'e' or 'E' && (char.IsAsciiDigit(text[position + 1]) || (position + 2 < text.Length && text[position + 1] is '+' or '-' && char.IsAsciiDigit(text[position + 2]))))
        {
            position += 2;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        return text[..position];
    }
}
