using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class CssValueTests
{
    [Fact]
    public void AllPropertyGrammarsParse()
    {
        var failures = new List<string>();
        foreach (var property in CssPropertyRegistry.Properties)
        {
            if (property.AliasOf is null && property.Grammar.Length > 0 && CssPropertyRegistry.GetGrammar(property) is null)
            {
                failures.Add(property.Name + ": " + property.Grammar);
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    public static TheoryData<string, int> ColorTests()
    {
        var data = new TheoryData<string, int>();
        foreach (var file in Directory.GetFiles(TestPaths.External("css-parsing-tests"), "color_*.json").Order(StringComparer.Ordinal))
        {
            var tests = JsonNode.Parse(File.ReadAllText(file))!.AsArray();
            for (var i = 0; i < tests.Count / 2; i++)
            {
                data.Add(Path.GetFileName(file), i);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ColorTests))]
    public void Color(string file, int index)
    {
        var tests = TestJson.Load(TestPaths.External("css-parsing-tests", file));
        var input = tests[index * 2].GetString()!;
        Assert.SkipWhen(tests[(index * 2) + 1].ValueKind == JsonValueKind.Array, "light-dark() colors are not resolved");
        var expected = tests[(index * 2) + 1].GetString();

        // The test suite predates the serialization of the legacy colors defined by CSS Color 4: their channels are
        // rounded, and a color with a none component keeps its color function. The suite resolves none to 0.
        if (System.Text.RegularExpressions.Regex.IsMatch(input, "^(rgba?|hsla?|hwb)\\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.ExplicitCapture))
        {
            input = System.Text.RegularExpressions.Regex.Replace(input, "\\bnone\\b", "0", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // The test suite also predates the clamping of the lightness of lab(), lch(), oklab() and oklch() and of the chroma of
        // lch() and oklch() (https://drafts.csswg.org/css-color-4/#specifying-lab-lch)
        if (expected is not null && System.Text.RegularExpressions.Regex.Match(expected, @"^(?<name>(ok)?(lab|lch))\((?<l>-?[0-9.]+) (?<c>-?[0-9.]+) ", System.Text.RegularExpressions.RegexOptions.ExplicitCapture) is { Success: true } lab)
        {
            var name = lab.Groups["name"].Value;
            var lightness = Math.Clamp(double.Parse(lab.Groups["l"].Value, System.Globalization.CultureInfo.InvariantCulture), 0, name.StartsWith("ok", StringComparison.Ordinal) ? 1 : 100);
            var chroma = double.Parse(lab.Groups["c"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (name.EndsWith("lch", StringComparison.Ordinal))
            {
                chroma = Math.Max(0, chroma);
            }

            expected = FormattableString.Invariant($"{name}({lightness} {chroma} ") + expected[lab.Length..];
        }

        var value = CssParser.ParseComponentValue(input, out _);
        var actual = value is null ? null : CssColor.Parse(value)?.Serialize(roundLegacyChannels: false);

        // The alpha of a hex color is an 8-bit value serialized with 2 or 3 decimals (the suite uses 6 decimals)
        Assert.True(SerializationEquals(expected, actual, input.StartsWith('#', StringComparison.Ordinal) ? 1 / 510.0 : 0), $"Input: {input}\nExpected: {expected}\nActual:   {actual}");
    }

    // The numbers are compared with a tolerance: the test suite rounds some values differently in the last decimal, and the
    // numbers are serialized with 6 significant digits
    private static bool SerializationEquals(string? expected, string? actual, double tolerance)
    {
        if (expected is null || actual is null)
            return expected == actual;

        var expectedParts = System.Text.RegularExpressions.Regex.Split(expected, @"(-?[0-9.]+)");
        var actualParts = System.Text.RegularExpressions.Regex.Split(actual, @"(-?[0-9.]+)");
        if (expectedParts.Length != actualParts.Length)
            return false;

        for (var i = 0; i < expectedParts.Length; i++)
        {
            if (expectedParts[i] == actualParts[i])
                continue;

            if (!double.TryParse(expectedParts[i], System.Globalization.CultureInfo.InvariantCulture, out var a) || !double.TryParse(actualParts[i], System.Globalization.CultureInfo.InvariantCulture, out var b) || Math.Abs(a - b) > Math.Max(Math.Max(1.5e-6, Math.Abs(a) * 5e-6), tolerance))
                return false;
        }

        return true;
    }

    [Theory]
    [InlineData("margin", "1px 2px", true)]
    [InlineData("margin", "1px 2px 3px 4px 5px", false)]
    [InlineData("color", "red", true)]
    [InlineData("color", "rgb(1, 2, 3)", true)]
    [InlineData("color", "10px", false)]
    [InlineData("display", "block flow", true)]
    [InlineData("display", "blocky", false)]
    [InlineData("width", "calc(100% - 10px)", true)]
    [InlineData("width", "-10px", false)]
    [InlineData("background", "url(a.png) no-repeat red", true)]
    [InlineData("font", "bold 12px/1.5 Arial, sans-serif", true)]
    [InlineData("border", "1px solid red", true)]
    [InlineData("grid-template-columns", "repeat(2, 1fr)", true)]
    [InlineData("transition", "opacity 1s ease-in, color 2s", true)]
    [InlineData("d", "none", true)]
    [InlineData("d", "path('M 0 0 L 10 10')", true)]
    [InlineData("d", "path('M 0 0 L 10')", false)] // The path data must not contain errors
    [InlineData("d", "path('')", false)]
    [InlineData("d", "path(evenodd, 'M 0 0')", false)]
    [InlineData("d", "'M 0 0'", false)]
    [InlineData("clip-path", "path(evenodd, 'M 0 0 h 10')", true)]
    [InlineData("clip-path", "path(evenodd, 'M 0 0 h')", false)]
    [InlineData("offset-path", "path('M 0 0 h 10')", true)]
    [InlineData("offset-path", "path(evenodd, 'M 0 0 h 10')", false)] // The fill rule does not apply to a motion path
    [InlineData("offset-path", "polygon(evenodd, 1px 2px)", true)]
    [InlineData("offset-path", "circle(10px) border-box", true)]
    public void Validation(string property, string value, bool valid)
    {
        var definition = CssPropertyRegistry.Get(property)!;
        var grammar = CssPropertyRegistry.GetGrammar(definition)!;
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        Assert.Equal(valid, ValueMatcher.Matches(grammar, values));
    }
}
