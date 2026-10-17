using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Css;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// CSS style sheets and declaration lists: the shared invariants, the statements of the root reproducing the text, and
/// the value helpers of <see cref="CssExtensions"/> on every token the parser produced.
/// </summary>
internal static class CssFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "@media ", "@supports ", "@import ", "@font-face", "@keyframes ", "@layer ", "@container ", "@page", "@namespace ", "@charset \"utf-8\";", "@scope", "@property --x",
        "unicode-range", "U+0025-00FF", "u+4??", "!important", "url(", "url(\"", "/*", "*/", "<!--", "-->", ":hover", "::before", ":nth-child(", ":nth-last-of-type(", "2n+1", "-n+3", "odd", "even", " of ",
        ":is(", ":where(", ":not(", ":has(", ":lang(", ":host(", "::part(", "[", "]", "=", "~=", "|=", "^=", "$=", "*=", " i]", " s]", "||", "|", ">", "+", "~", "&", "*",
        "{", "}", "(", ")", ";", ":", ",", "\\", "\\31 ", "\"", "'", "#", "#id", ".", ".cls", "-", "--", "--x", "1e3", "-.5", "10px", "50%", "1\\70x",
        "calc(", "var(--x)", "and", "or", "not", "only", "screen", "(min-width: 10px)", "(width >= 600px)", "<", "<=", ">=", "from", "to", "@", "\n", "\t", "\f", "\r\n", " ",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-css", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>Byte 0: bit 0 parses a declaration list instead of a style sheet. Byte 1: the nesting limit. Then the CSS.</summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var depth = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        var options = new CssParseOptions
        {
            SourceKind = (flags & 1) == 0 ? CssSourceKind.StyleSheet : CssSourceKind.DeclarationList,
            MaxDepth = LanguageFuzzInput.ReadDepth(depth),
        };

        var tree = CssSyntaxTree.ParseText(text, options);
        var shape = SyntaxTreeInvariants.Check(tree, text, "CSS", diagnosticsOnNodes: true, diagnosticsInSourceOrder: true);
        var root = tree.GetRoot();
        FuzzAssert.Equal(text, root.Statements.ToFullString() + root.EndOfFileToken.ToFullString(), "The statements and the end of the file do not reproduce the text");

        foreach (var token in shape.Tokens)
        {
            CheckToken(token);
        }

        var walker = new CountingWalker();
        try
        {
            walker.Visit(root);
        }
        catch (InsufficientExecutionStackException)
        {
            // Documented by the walker for a tree nested too deeply to walk
            return;
        }

        SyntaxTreeInvariants.CheckWalker(shape, walker.TokenCount, walker.TriviaCount);
    }

    private static void CheckToken(SyntaxToken token)
    {
        var kind = token.Kind();
        var number = token.GetNumericValue();
        var isNumeric = SyntaxFacts.IsNumeric(kind);
        if (isNumeric && !token.IsMissing)
        {
            FuzzAssert.True(number is not null, $"The numeric token {FuzzAssert.Format(token.Text)} has no value");
            FuzzAssert.False(double.IsNaN(number.Value), $"The numeric token {FuzzAssert.Format(token.Text)} is NaN");
        }
        else if (!isNumeric)
        {
            FuzzAssert.True(number is null, $"The {kind} {FuzzAssert.Format(token.Text)} has a numeric value");
        }

        if (token.IsInteger())
        {
            FuzzAssert.True(isNumeric, $"The {kind} {FuzzAssert.Format(token.Text)} is an integer");
            if (number is { } value)
            {
                FuzzAssert.True(double.IsInteger(value) || double.IsInfinity(value), $"The integer token {FuzzAssert.Format(token.Text)} has the value {value}");
            }
        }

        var unit = token.GetUnit();
        if (kind is SyntaxKind.DimensionToken && !token.IsMissing)
        {
            FuzzAssert.False(string.IsNullOrEmpty(unit), $"The dimension token {FuzzAssert.Format(token.Text)} has no unit");
        }
        else if (kind is not SyntaxKind.DimensionToken)
        {
            FuzzAssert.True(unit is null, $"The {kind} {FuzzAssert.Format(token.Text)} has a unit");
        }

        _ = token.IsIdHash();
        if (token.GetUnicodeRange() is { } range)
        {
            FuzzAssert.True(kind is SyntaxKind.UnicodeRangeToken, $"The {kind} {FuzzAssert.Format(token.Text)} has a unicode range");
            FuzzAssert.True(range.Start >= 0 && range.End >= 0, $"The unicode-range token {FuzzAssert.Format(token.Text)} has the range {range.Start}-{range.End}");
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        string[] files = ["component_value_list.json", "one_component_value.json", "one_declaration.json", "blocks_contents.json", "one_rule.json", "stylesheet.json", "An+B.json"];
        foreach (var file in files)
        {
            byte flags = file is "one_declaration.json" or "blocks_contents.json" ? (byte)1 : (byte)0;
            using var document = FuzzResources.ReadJson(typeof(CssFuzzTargets).Assembly, "css-parsing-tests." + file);
            var cases = document.RootElement;
            for (var i = 0; i + 1 < cases.GetArrayLength(); i += 2)
            {
                var input = cases[i].GetString()!;

                // The An+B cases are the argument of a pseudo-class, which is where the parser reads them
                var text = file is "An+B.json" ? "li:nth-child(" + input + ") {}" : input;
                yield return FuzzSeed.FromText(FormattableString.Invariant($"{file}-{i / 2}"), [flags, 0], text);
            }
        }

        yield return FuzzSeed.FromText("media", [0, 0], "@media screen and (min-width: 600px), print and (orientation: landscape) { .a > .b + .c ~ .d { color: red !important } }");
        yield return FuzzSeed.FromText("supports", [0, 0], "@supports (display: grid) and (not (display: inline-grid)) { div { display: grid } }\n@supports selector(:has(a)) {}");
        yield return FuzzSeed.FromText("font-face", [0, 0], "@font-face { font-family: \"X\"; src: url(x.woff2) format(\"woff2\"); unicode-range: U+0025-00FF, u+4??; }");
        yield return FuzzSeed.FromText("keyframes", [0, 0], "@keyframes spin { from { transform: rotate(0deg) } 50% { opacity: .5 } to { transform: rotate(1turn) } }");
        yield return FuzzSeed.FromText("layers", [0, 0], "@import url(\"a.css\") layer(base) supports(display: grid) screen;\n@layer base, components;\n@layer base { html { color: black } }");
        yield return FuzzSeed.FromText("container", [0, 0], "@container sidebar (width >= 400px) and style(--responsive: true) { .card { display: flex } }");
        yield return FuzzSeed.FromText("nesting", [0, 0], ".card { color: red; &:hover { color: blue } .child & { margin: 0 } > p { padding: 1px 2px } @media (width < 10em) { color: green } }");
        yield return FuzzSeed.FromText("selectors", [0, 0], "a[href^='http' i], input:not([type=\"text\" s]), li:nth-child(2n+1 of .x), :is(h1, h2)::before, svg|rect, *|*, #id.cls:where(:focus-visible), ns|e[ns|attr] {}");
        yield return FuzzSeed.FromText("escapes", [0, 0], ".\\31 23 { content: \"\\201C\\\"\"; width: calc(100% - 2 * var(--gap, 1em)); } /* comment */ <!-- --> #\\@x {}");
        yield return FuzzSeed.FromText("page", [0, 0], "@charset \"utf-8\";\n@namespace svg url(http://www.w3.org/2000/svg);\n@page :first { margin: 1in; @top-left { content: \"x\" } }");
        yield return FuzzSeed.FromText("declarations", [1, 0], "color: red; background: url(a.png) no-repeat; --custom: { a b }; margin: -1.5e2px 50% 0 auto !important");
        yield return FuzzSeed.FromText("deep", [0, 1], "a { b { c { d { color: red } } } } @media (a) { @media (b) { x { y: z } } }");
    }

    private sealed class CountingWalker() : CssSyntaxWalker(SyntaxWalkerDepth.Trivia)
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
