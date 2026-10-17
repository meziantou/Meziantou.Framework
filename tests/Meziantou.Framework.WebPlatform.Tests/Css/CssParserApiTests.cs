using CssApi = Meziantou.Framework.WebPlatform.Css.Om.Css;
using Meziantou.Framework.WebPlatform.Css.ParserApi;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The CSS Parser API (https://wicg.github.io/css-parser-api/), on the entry points of CSS Syntax.</summary>
public sealed class CssParserApiTests
{
    // A readable description of the tokens: K(ident), U(value unit), F(...), B(...), "text" for the other tokens
    private static string Describe(IEnumerable<CssParserToken> tokens) => string.Join(" ", tokens.Select(Describe));

    private static string Describe(CssParserToken token) => token.Value switch
    {
        string text => "\"" + text + "\"",
        CssKeywordValue keyword => "K(" + keyword.Value + ")",
        CssUnitValue unit => "U(" + unit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + unit.Unit + ")",
        CssParserFunction function => "F" + function.Name + "(" + string.Join(" | ", function.Args.Select(Describe)) + ")",
        CssParserBlock block => "B" + block.Name[0] + Describe(block.Body) + block.Name[1],
        _ => throw new InvalidOperationException(),
    };

    [Fact]
    public async Task ParseStylesheet_ReturnsTheRules()
    {
        var rules = await CssApi.ParseStylesheet("<!-- @import url(a.css); a { color: red; b { c: d } } -->");

        Assert.Equal(2, rules.Count);
        var import = Assert.IsType<CssParserAtRule>(rules[0]);
        Assert.Equal("import", import.Name);
        Assert.Equal("\" \" \"url(a.css)\"", Describe(import.Prelude));
        Assert.Null(import.Body);
        Assert.Equal("@import url(a.css);", import.ToString());

        var style = Assert.IsType<CssParserQualifiedRule>(rules[1]);
        Assert.Equal("K(a) \" \"", Describe(style.Prelude));
        Assert.Equal(2, style.Body.Count);
        var color = Assert.IsType<CssParserDeclaration>(style.Body[0]);
        Assert.Equal("color", color.Name);
        Assert.Equal("K(red)", Describe(color.Body));
        var nested = Assert.IsType<CssParserQualifiedRule>(style.Body[1]);
        Assert.Equal("K(b) \" \"", Describe(nested.Prelude));
        Assert.Equal("a { color: red; b { c: d; } }", style.ToString());
    }

    [Fact]
    public async Task ParseRuleList_ReturnsTheRules()
    {
        var rules = await CssApi.ParseRuleList("@media screen { a { b: c } } d {}");

        Assert.Equal(["@media screen { a { b: c; } }", "d {}"], rules.Select(rule => rule.ToString()));
        var media = Assert.IsType<CssParserAtRule>(rules[0]);
        Assert.Equal("\" \" K(screen) \" \"", Describe(media.Prelude));
        Assert.IsType<CssParserQualifiedRule>(Assert.Single(media.Body!));
    }

    [Theory]
    [InlineData("  @media screen { a { b: c } }  ", "@media screen { a { b: c; } }")]
    [InlineData("@charset \"utf-8\";", "@charset \"utf-8\";")]
    [InlineData("a, b > c { d: e !important }", "a, b > c { d: e !important; }")]
    public async Task ParseRule_ReturnsTheRule(string css, string expected)
    {
        var rule = await CssApi.ParseRule(css);

        Assert.Equal(expected, rule.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a {} b {}")]
    [InlineData("a")]
    [InlineData("--custom: value {}")]
    public async Task ParseRule_RejectsInvalidInput(string css)
    {
        var task = CssApi.ParseRule(css);

        var exception = await Assert.ThrowsAsync<DomException>(() => task);
        Assert.Equal("SyntaxError", exception.Name);
    }

    [Fact]
    public async Task ParseDeclarationList_ReturnsTheDeclarations()
    {
        // https://drafts.csswg.org/css-syntax-3/#parse-list-of-declarations: the at-rules and the invalid declarations are dropped
        var declarations = await CssApi.ParseDeclarationList("a: b; @media x { y: z } c : d !important; e; f: {g}; h: 1px {i}");

        Assert.Equal(["a: b", "c: d !important", "f: {g}"], declarations.Select(declaration => declaration.ToString()));
        Assert.All(declarations, declaration => Assert.IsType<CssParserDeclaration>(declaration));
        Assert.True(((CssParserDeclaration)declarations[1]).Important);
    }

    [Fact]
    public void ParseDeclaration_ReturnsTheDeclaration()
    {
        var declaration = CssApi.ParseDeclaration("  color : rgb(1 2 3) ! IMPORTANT ");

        Assert.Equal("color", declaration.Name);
        Assert.Equal("Frgb(U(1 number) \" \" U(2 number) \" \" U(3 number))", Describe(declaration.Body));
        Assert.True(declaration.Important);
        Assert.Equal("color: rgb(1 2 3) !important", declaration.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("color")]
    [InlineData("1px: a")]
    [InlineData("color: red;")]
    [InlineData("a: b; c: d")]
    public void ParseDeclaration_ThrowsForInvalidInput(string css)
    {
        var exception = Assert.Throws<DomException>(() => CssApi.ParseDeclaration(css));
        Assert.Equal("SyntaxError", exception.Name);
    }

    [Fact]
    public void ParseValueList_ConvertsTheTokens()
    {
        var values = CssApi.ParseValueList("a 10PX 50% +3 10foo 5number #fff 'x', f(1, 2) [x]{}(y) url(a.png) /* comment */ @b");

        Assert.Equal(
            "K(a) \" \" U(10 px) \" \" U(50 percent) \" \" U(3 number) \" \" \"10foo\" \" \" \"5number\" \" \" \"#fff\" \" \" \"'x'\" \",\" \" \" Ff(U(1 number) | \" \" U(2 number)) \" \" B[K(x)] B{} B(K(y)) \" \" \"url(a.png)\" \" \" \" \" \"@b\"",
            Describe(values));
    }

    [Fact]
    public void ParseValueList_KeepsTheEscapesOfTheTextTokens()
    {
        var values = CssApi.ParseValueList("\\61 b \"\\41\" #\\31 23");

        Assert.Equal("K(ab) \" \" \"\"\\41\"\" \" \" \"#\\31 23\"", Describe(values));
        Assert.Equal("ab", Assert.IsType<CssKeywordValue>(values[0].Value).Value);
    }

    [Fact]
    public void ParseValue_ReturnsASingleComponentValue()
    {
        var function = Assert.IsType<CssParserFunction>(CssApi.ParseValue("  calc(1px + 2%, (a))  ").Value);

        Assert.Equal("calc", function.Name);
        Assert.Equal(2, function.Args.Count);
        Assert.Equal("U(1 px) \" \" \"+\" \" \" U(2 percent)", Describe(function.Args[0]));
        Assert.Equal("\" \" B(K(a))", Describe(function.Args[1]));
        Assert.Equal("calc(1px + 2%, (a))", function.ToString());
        Assert.Equal("Ff(K(a) | )", Describe(CssApi.ParseValue("f(a,)")));
        Assert.Equal("f(a,)", CssApi.ParseValue("f(a,)").ToString());
        Assert.Equal("Ff()", Describe(CssApi.ParseValue("f()")));
        Assert.Equal("U(5 em)", Describe(CssApi.ParseValue("5em")));
        Assert.Equal("\";\"", Describe(CssApi.ParseValue(";")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a b")]
    [InlineData("f() g()")]
    public void ParseValue_ThrowsForInvalidInput(string css)
    {
        var exception = Assert.Throws<DomException>(() => CssApi.ParseValue(css));
        Assert.Equal("SyntaxError", exception.Name);
    }

    [Theory]
    [InlineData("a, b c,", "[K(a)] [\" \" K(b) \" \" K(c)]")]
    [InlineData(",a", "[] [K(a)]")]
    [InlineData("f(a, b), [c, d]", "[Ff(K(a) | \" \" K(b))] [\" \" B[K(c) \",\" \" \" K(d)]]")]
    [InlineData("", "")]
    public void ParseCommaValueList_SplitsAtTheTopLevelCommas(string css, string expected)
    {
        var groups = CssApi.ParseCommaValueList(css);

        Assert.Equal(expected, string.Join(" ", groups.Select(group => "[" + Describe(group) + "]")));
    }

    [Fact]
    public async Task AtRules_SelectTheProductionOfTheBlocks()
    {
        var options = new CssParserOptions
        {
            AtRules = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["font-face"] = "declaration-list",
                ["KEYFRAMES"] = "qualified-rule-list",
                ["page"] = "declaration-rule-list",
                ["media"] = "rule-list",
                ["layer"] = "at-rule-list",
            },
        };

        var rules = await CssApi.ParseStylesheet("""
            @font-face { a { } src: x; @b; }
            @keyframes k { from { a: b } c: d; @e; }
            @page { margin: 0; @top-left { content: "" } p { } }
            @media print { a { } b: c; @d; }
            @layer x { a { } b: c; @d; }
            @unknown { a: b; c { } @d; }
            """, options);

        Assert.Equal(
        [
            "@font-face { src: x; }",
            "@keyframes k { from { a: b; } }",
            "@page { margin: 0; @top-left { content: \"\"; } }",
            "@media print { a {} @d; }",
            "@layer x { @d; }",
            "@unknown { a: b; c {} @d; }",
        ], rules.Select(rule => rule.ToString()));
    }

    [Fact]
    public void AtRules_ThrowForAnUnknownProduction()
    {
        var options = new CssParserOptions { AtRules = [new("media", "rules")] };

        Assert.Throws<TypeErrorException>(() => { _ = CssApi.ParseStylesheet("@media {}", options); });
    }

    [Fact]
    public void Constructors_CreateTheObjects()
    {
        var block = new CssParserBlock("()", [new CssUnitValue(1, "px"), " ", "a"]);
        var function = new CssParserFunction("rgb", [[new CssUnitValue(1, "number")], [" ", block]]);
        var declaration = new CssParserDeclaration("color", [function]);
        var rule = new CssParserQualifiedRule([new CssKeywordValue("a"), " "], [declaration]);
        var atRule = new CssParserAtRule("media", [" ", "print", " "], [rule]);

        Assert.Equal("(1px a)", block.ToString());
        Assert.Equal("rgb(1, (1px a))", function.ToString());
        Assert.Equal("@media print { a { color: rgb(1, (1px a)); } }", atRule.ToString());
        Assert.Equal("@import;", new CssParserAtRule("import", []).ToString());
        Assert.Equal("a:", new CssParserDeclaration("a").ToString());
        Assert.Empty(new CssParserQualifiedRule([]).Body);
    }

    [Fact]
    public void Serialization_SeparatesTheTokensThatWouldBeParsedAsOne()
    {
        var block = new CssParserBlock("[]", [new CssKeywordValue("a"), new CssUnitValue(1, "number"), new CssKeywordValue("b"), "%"]);

        Assert.Equal("[a/**/1/**/b/**/%]", block.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("[")]
    [InlineData("<>")]
    public void Block_ThrowsForAnInvalidName(string name)
    {
        Assert.Throws<TypeErrorException>(() => new CssParserBlock(name, []));
    }
}
