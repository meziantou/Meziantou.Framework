using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>
/// The counter styles (https://drafts.csswg.org/css-counter-styles-3/): the predefined styles, the @counter-style rules and the
/// symbols() function, in the markers of the list items and in the counter() and counters() functions.
/// </summary>
public sealed partial class CounterStyleTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html, bool scripting = false) => _contexts.Create(new BrowsingContextOptions { ScriptingEnabled = scripting, Viewport = new ViewportOptions { Width = 800, Height = 600 } }).Navigate(_ => "<!DOCTYPE html>" + html);

    [GeneratedRegex("""^\s*generated-text "(?<text>.*)"$""", RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    private static partial Regex TextRegex();

    /// <summary>The generated texts of the box tree of the document (the markers and the generated content), separated by a vertical bar.</summary>
    private static string Texts(Document document) => string.Join('|', TextRegex().Matches(BoxTree.Build(document).Dump()).Select(match => match.Groups["text"].Value));

    private string Texts(string html) => Texts(Parse(html));

    /// <summary>The representation of a counter value in a predefined counter style, as code points in hexadecimal (ASCII characters as is).</summary>
    private static string Generate(string style, long value, WritingMode writingMode = WritingMode.HorizontalTb, Direction direction = Direction.Ltr)
    {
        var representation = CounterStyle.Predefined[style].Generate(value, writingMode, direction);
        if (representation is null)
            return "(fallback)";

        return string.Join(' ', representation.EnumerateRunes().Select(rune => rune.IsAscii && rune.Value > ' ' ? rune.ToString() : rune.Value.ToString("X4", CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("decimal", 0, "0")]
    [InlineData("decimal", -12, "- 1 2")]
    [InlineData("decimal-leading-zero", 7, "0 7")]
    [InlineData("decimal-leading-zero", -7, "- 7")]
    [InlineData("decimal-leading-zero", 123, "1 2 3")]
    [InlineData("lower-roman", 3999, "m m m c m x c i x")]
    [InlineData("lower-roman", 4000, "(fallback)")]
    [InlineData("lower-roman", 0, "(fallback)")]
    [InlineData("upper-roman", 14, "X I V")]
    [InlineData("lower-alpha", 27, "a a")]
    [InlineData("upper-latin", 52, "A Z")]
    [InlineData("lower-alpha", 0, "(fallback)")]
    [InlineData("lower-greek", 25, "03B1 03B1")]
    [InlineData("arabic-indic", 10, "0661 0660")]
    [InlineData("persian", 42, "06F4 06F2")]
    [InlineData("devanagari", -3, "- 0969")]
    [InlineData("khmer", 5, "17E5")]
    [InlineData("cjk-decimal", 105, "4E00 3007 4E94")]
    [InlineData("cjk-decimal", -1, "(fallback)")]
    [InlineData("armenian", 1984, "054C 054B 0541 0534")]
    [InlineData("lower-armenian", 15, "056A 0565")]
    [InlineData("upper-armenian", 10000, "(fallback)")]
    [InlineData("georgian", 19999, "10F5 10F0 10E8 10DF 10D7")]
    [InlineData("hebrew", 16, "05D8 05D6")]
    [InlineData("hebrew", 1001, "05D0 05F3 05D0")]
    [InlineData("hebrew", 11000, "(fallback)")]
    [InlineData("hiragana", 49, "3042 3042")]
    [InlineData("katakana-iroha", 1, "30A4")]
    [InlineData("cjk-earthly-branch", 12, "4EA5")]
    [InlineData("cjk-earthly-branch", 13, "(fallback)")]
    [InlineData("cjk-heavenly-stem", 1, "7532")]
    [InlineData("disc", 5, "2022")]
    [InlineData("circle", -5, "25E6")]
    [InlineData("square", 0, "25AA")]
    public void PredefinedStyles(string style, long value, string expected) => Assert.Equal(expected, Generate(style, value));

    [Theory]

    // The Chinese styles: one zero for the zeros between two digits, no zero at the end of a group
    [InlineData("simp-chinese-informal", 0, "96F6")]
    [InlineData("simp-chinese-informal", 10, "5341")]
    [InlineData("simp-chinese-informal", 11, "5341 4E00")]
    [InlineData("simp-chinese-informal", 110, "4E00 767E 4E00 5341")]
    [InlineData("simp-chinese-informal", 1001, "4E00 5343 96F6 4E00")]
    [InlineData("simp-chinese-informal", 10010, "4E00 4E07 96F6 5341")]
    [InlineData("simp-chinese-informal", 11000, "4E00 4E07 4E00 5343")]
    [InlineData("simp-chinese-informal", -3, "8D1F 4E09")]
    [InlineData("simp-chinese-formal", 10, "58F9 62FE")]
    [InlineData("trad-chinese-informal", 100000001, "4E00 5104 96F6 4E00")]
    [InlineData("trad-chinese-formal", -20, "8CA0 8CB3 62FE")]
    [InlineData("cjk-ideographic", 10000, "4E00 842C")]
    [InlineData("simp-chinese-informal", 1000000000000, "4E00 4E07 4EBF")]
    [InlineData("simp-chinese-informal", 10000000000000000, "(fallback)")]

    // The Japanese styles: no zero; the informal style omits the 1 before the markers of the digits
    [InlineData("japanese-informal", 0, "3007")]
    [InlineData("japanese-informal", 1111, "5343 767E 5341 4E00")]
    [InlineData("japanese-informal", 11000, "4E00 4E07 5343")]
    [InlineData("japanese-informal", 10010, "4E00 4E07 5341")]
    [InlineData("japanese-formal", 1010, "58F1 9621 58F1 62FE")]
    [InlineData("japanese-formal", -5, "30DE 30A4 30CA 30B9 4F0D")]

    // The Korean styles: the groups are separated by a space; the informal style omits the 1 of the ten thousands
    [InlineData("korean-hangul-formal", 10001, "C77C B9CC 0020 C77C")]
    [InlineData("korean-hangul-formal", 1000, "C77C CC9C")]
    [InlineData("korean-hanja-informal", 10000, "842C")]
    [InlineData("korean-hanja-informal", 20000, "4E8C 842C")]
    [InlineData("korean-hanja-informal", 100001111, "4E00 5104 0020 5343 767E 5341 4E00")]
    [InlineData("korean-hanja-formal", -11, "B9C8 C774 B108 C2A4 0020 58F9 62FE 58F9")]

    // The Ethiopic numbers: groups of two digits
    [InlineData("ethiopic-numeric", 1, "1369")]
    [InlineData("ethiopic-numeric", 100, "137B")]
    [InlineData("ethiopic-numeric", 78010092, "1378 1370 137B 1369 137C 137A 136A")]
    [InlineData("ethiopic-numeric", 780100000092, "1378 1370 137B 1369 137C 137C 137A 136A")]
    [InlineData("ethiopic-numeric", 0, "(fallback)")]
    public void ComplexPredefinedStyles(string style, long value, string expected) => Assert.Equal(expected, Generate(style, value));

    [Theory]
    [InlineData("disclosure-closed", WritingMode.HorizontalTb, Direction.Ltr, "25B8")]
    [InlineData("disclosure-closed", WritingMode.HorizontalTb, Direction.Rtl, "25C2")]
    [InlineData("disclosure-closed", WritingMode.VerticalRl, Direction.Ltr, "25BE")]
    [InlineData("disclosure-closed", WritingMode.VerticalLr, Direction.Rtl, "25B4")]
    [InlineData("disclosure-open", WritingMode.HorizontalTb, Direction.Rtl, "25BE")]
    [InlineData("disclosure-open", WritingMode.VerticalRl, Direction.Ltr, "25C2")]
    [InlineData("disclosure-open", WritingMode.VerticalLr, Direction.Ltr, "25B8")]
    public void DisclosureStyles(string style, object writingMode, object direction, string expected) => Assert.Equal(expected, Generate(style, 1, (WritingMode)writingMode, (Direction)direction));

    [Fact]
    public void PredefinedStyles_PrefixAndSuffix()
    {
        Assert.Equal(". ", CounterStyle.Predefined["decimal"].Suffix);
        Assert.Equal(" ", CounterStyle.Predefined["disc"].Suffix);
        Assert.Equal(", ", CounterStyle.Predefined["korean-hangul-formal"].Suffix);
        Assert.Equal("/ ", CounterStyle.Predefined["ethiopic-numeric"].Suffix);
        Assert.Equal(0x3001, Assert.Single(CounterStyle.Predefined["hiragana"].Suffix));
        Assert.Equal("", CounterStyle.Predefined["cjk-decimal"].Prefix);
    }

    [Theory]
    [InlineData("system: cyclic; symbols: A B C", "A. |B. |C. |A. |B. ")]
    [InlineData("system: fixed; symbols: A B C", "A. |B. |C. |4. |5. ")]
    [InlineData("system: fixed 3; symbols: A B; fallback: upper-roman", "I. |II. |A. |B. |V. ")]
    [InlineData("system: symbolic; symbols: '*' '+'", "*. |+. |**. |++. |***. ")]
    [InlineData("symbols: '*' '+'", "*. |+. |**. |++. |***. ")]
    [InlineData("system: alphabetic; symbols: x y", "x. |y. |xx. |xy. |yx. ")]
    [InlineData("system: numeric; symbols: a b c", "b. |c. |ba. |bb. |bc. ")]
    [InlineData("system: additive; additive-symbols: 3 c, 2 b, 1 a", "a. |b. |c. |ca. |cb. ")]
    [InlineData("system: additive; additive-symbols: 2 b", "1. |b. |3. |bb. |5. ")]
    [InlineData("system: cyclic; symbols: A; prefix: '('; suffix: ')'", "(A)|(A)|(A)|(A)|(A)")]
    [InlineData("system: numeric; symbols: '0' '1'; pad: 3 'x'", "xx1. |x10. |x11. |100. |101. ")]
    [InlineData("system: extends decimal; range: 2 3, 5 infinite; fallback: lower-alpha", "a. |2. |3. |d. |5. ")]
    [InlineData("system: extends upper-roman; suffix: ') '", "I) |II) |III) |IV) |V) ")]
    [InlineData("system: extends unknown; prefix: '#'", "#1. |#2. |#3. |#4. |#5. ")]

    // The rules that do not define a counter style: the name is unknown, the decimal style is used
    [InlineData("system: numeric; symbols: A", "1. |2. |3. |4. |5. ")]
    [InlineData("system: additive; symbols: A B", "1. |2. |3. |4. |5. ")]
    [InlineData("system: extends decimal; symbols: A B", "1. |2. |3. |4. |5. ")]
    [InlineData("system: cyclic", "1. |2. |3. |4. |5. ")]

    // The math functions of the integers
    [InlineData("system: fixed calc(1 + 1); symbols: A B C", "1. |A. |B. |C. |5. ")]
    [InlineData("system: extends decimal; pad: calc(1 + sign(1em)) '0'", "01. |02. |03. |04. |05. ")]
    public void CounterStyleRule(string descriptors, string expected)
        => Assert.Equal(expected, Texts("<style>@counter-style foo { " + descriptors + " } ol { list-style: foo inside }</style><ol><li><li><li><li><li></ol>"));

    [Theory]
    [InlineData("system: numeric; symbols: '0' '1'; negative: '(' ')'", "(10). |(1). |0. |1. ")]
    [InlineData("system: extends decimal; pad: 4 '0'", "-002. |-001. |0000. |0001. ")]
    [InlineData("system: extends decimal; pad: 4 '0'; negative: '(' ')'", "(02). |(01). |0000. |0001. ")]
    [InlineData("system: cyclic; symbols: A B C", "A. |B. |C. |A. ")]
    [InlineData("system: symbolic; symbols: A B C", "-2. |-1. |0. |A. ")]
    [InlineData("system: additive; additive-symbols: 1 I, 0 O; range: infinite infinite", "-II. |-I. |O. |I. ")]
    public void CounterStyleRule_NegativeValues(string descriptors, string expected)
        => Assert.Equal(expected, Texts("<style>@counter-style foo { " + descriptors + " } ol { list-style: foo inside }</style><ol start=-2><li><li><li><li></ol>"));

    [Fact]
    public void CounterStyleRule_LongRepresentationsUseTheFallback()
        => Assert.Equal("*. |121. |***. ", Texts("<style>@counter-style foo { system: symbolic; symbols: '*' } li { list-style: foo inside }</style><ol><li value=1><li value=121><li value=3></ol>"));

    [Fact]
    public void CounterStyleRule_ExtendsCycleIsDecimal()
        => Assert.Equal("(1. |[1. |a. ", Texts("""
            <style>
              @counter-style a { system: extends b; prefix: '(' }
              @counter-style b { system: extends a; prefix: '['; suffix: '! ' }
              @counter-style c { system: extends c }
              @counter-style d { system: extends lower-alpha; fallback: d }
              ol { list-style-position: inside }
            </style>
            <ol><li style="list-style-type: a"></ol><ol><li style="list-style-type: b"></ol><ol><li style="list-style-type: d"></ol>
            """).Replace("! ", ". ", StringComparison.Ordinal));

    [Fact]
    public void CounterStyleRule_FallbackLoopIsDecimal()
        => Assert.Equal("A. |3. ", Texts("""
            <style>
              @counter-style a { system: fixed; symbols: A; fallback: b }
              @counter-style b { system: fixed; symbols: B; fallback: a }
            </style>
            <ol style="list-style: a inside"><li><li value=3></ol>
            """));

    [Fact]
    public void CounterStyleRule_LastRuleWins_AndLayers()
        => Assert.Equal("C. |X. ", Texts("""
            <style>
              @counter-style a { system: cyclic; symbols: A }
              @counter-style a { system: cyclic; symbols: B }
              @counter-style a { system: cyclic; symbols: C }
              @layer l { @counter-style b { system: cyclic; symbols: Y } }
              @counter-style b { system: cyclic; symbols: X }
              @layer l { @counter-style b { system: cyclic; symbols: Z } }
            </style>
            <ol style="list-style: a inside"><li></ol><ol style="list-style: b inside"><li></ol>
            """));

    [Fact]
    public void CounterStyleRule_Names()
        => Assert.Equal("a. |*. |1. |I. |003. |(bullet) ", Texts("""
            <style>
              @counter-style foo { system: cyclic; symbols: '*' }
              @counter-style lower-alpha { system: extends upper-roman }
              @counter-style DECIMAL-leading-ZERO { system: extends decimal; pad: 3 '0' }
              @counter-style disc { system: cyclic; symbols: '!' }
              ol { list-style-position: inside }
            </style>
            <ol style="list-style-type: Lower-Latin"><li></ol>
            <ol style="list-style-type: foo"><li></ol>
            <ol style="list-style-type: Foo"><li></ol>
            <ol style="list-style-type: LOWER-ALPHA"><li></ol>
            <ol style="list-style-type: decimal-leading-zero"><li value=3></ol>
            <ol style="list-style-type: DISC"><li></ol>
            """).Replace("\\2022", "(bullet)", StringComparison.Ordinal));

    [Fact]
    public void SymbolsFunction()
        => Assert.Equal("* |** |A |B |b |ba |x |3 |1. ", Texts("""
            <style>ol { list-style-position: inside }</style>
            <ol style="list-style-type: symbols('*')"><li><li></ol>
            <ol style="list-style-type: symbols(cyclic 'A' 'B')"><li><li></ol>
            <ol style="list-style-type: symbols(numeric 'a' 'b')"><li><li></ol>
            <ol style="list-style-type: symbols(fixed 'x')" start=1><li><li value=3></ol>
            <ol style="list-style-type: symbols(alphabetic 'x')"><li></ol>
            """));

    [Fact]
    public void CounterFunctions()
        => Assert.Equal("[iii]|[a.b]|[*]|[7]", Texts("""
            <style>
              @counter-style foo { system: cyclic; symbols: '*'; prefix: '<'; suffix: '>' }
              div { counter-reset: c 3 }
              p { counter-reset: d 1; margin: 0 }
              span { counter-reset: d 2 }
              #a::before { content: '[' counter(c, lower-roman) ']' }
              #b::before { content: '[' counters(d, '.', lower-alpha) ']' }
              #c::before { content: '[' counter(c, foo) ']' }
              #d::before { content: '[' counter(e, unknown) ']'; counter-reset: e 7 }
            </style>
            <div><i id=a></i><p><span><i id=b></i></span></p><i id=c></i><i id=d></i></div>
            """));

    [Fact]
    public void Counters_DisplayContentsHasNoBox()
    {
        // The counter properties of an element without box have no effect, and the boxes of its contents are siblings of the
        // boxes of the contents of the next elements
        Assert.Equal("1|2|3", Texts("""
            <style>.c { counter-increment: s } .c::before { content: counter(s) }</style>
            <div style="display: contents"><div class=c></div></div>
            <div style="display: contents; counter-reset: s 5"><div class=c></div></div>
            <div style="display: contents; counter-set: s 9; counter-increment: s 2"><div class=c></div></div>
            """));
    }

    [Fact]
    public void Counters_IncrementThatOverflowsIsIgnored()
        => Assert.Equal("2147483646|2147483646|-2147483647|-2147483647", Texts("""
            <style>p::before { content: counter(c) } .up { counter-increment: c 5 } .down { counter-increment: c -5 }</style>
            <div style="counter-reset: c 2147483641"><p class=up></p><p class=up></p></div>
            <div style="counter-reset: c -2147483642"><p class=down></p><p class=down></p></div>
            """));

    [Fact]
    public void Counters_PseudoElementsAreChildrenOfTheirElement()
    {
        // The counter created by the ::before of an element is replaced by the counter-reset of a child of the element (a
        // following sibling box), not nested in it
        Assert.Equal("B|1|0", Texts("""
            <style>
              .b::before { counter-increment: c; content: 'B' }
              .r { counter-reset: c }
              .u::before { content: counters(c, '.') }
            </style>
            <span class=b><span class=u></span><span class=r><span class=u></span></span></span>
            """));
    }

    [Fact]
    public void TreeScopedNames()
    {
        // The name is looked up from the tree of the declaration: the style attribute of an element of the shadow tree, the
        // ::part() rule of the document; the rules of the document are visible in the shadow trees
        var document = Parse("""
            <style>
              @counter-style foo { system: cyclic; symbols: A }
              @counter-style bar { system: cyclic; symbols: B }
              #host::part(p) { list-style: foo inside }
            </style>
            <ol style="list-style: foo inside"><li></ol>
            <div id=host></div>
            """);
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.SetHtmlUnsafe("""
            <style>@counter-style foo { system: cyclic; symbols: S; fallback: bar }</style>
            <ol style="list-style: foo inside"><li></ol>
            <ol style="list-style: bar inside"><li></ol>
            <ol part=p><li></ol>
            """);
        Assert.Equal("A. |S. |B. |A. ", Texts(document));
    }

    [Fact]
    public void CssomChangesUpdateTheMarkers()
    {
        var document = Parse("<style>@counter-style foo { system: cyclic; symbols: A } @counter-style bar { system: fixed; symbols: X Y }</style><ol style='list-style: foo inside'><li></ol>");
        Assert.Equal("A. ", Texts(document));

        var sheet = (Meziantou.Framework.WebPlatform.Css.Om.CssStyleSheet)document.StyleSheets[0]!;
        var rule = (Meziantou.Framework.WebPlatform.Css.Om.CssCounterStyleRule)sheet.CssRules[0];
        rule.Symbols = "B C";
        rule.Suffix = "') '";
        Assert.Equal("B) ", Texts(document));

        // The values that are invalid, that change the algorithm, or that make the rule invalid are ignored
        rule.System = "numeric";
        rule.System = "extends decimal";
        rule.Symbols = "";
        rule.AdditiveSymbols = "1 A";
        Assert.Equal("cyclic", rule.System);
        Assert.Equal("B C", rule.Symbols);
        Assert.Equal("B) ", Texts(document));

        var bar = (Meziantou.Framework.WebPlatform.Css.Om.CssCounterStyleRule)sheet.CssRules[1];
        bar.System = "fixed 0";
        bar.Name = "foo";
        Assert.Equal("Y. ", Texts(document));

        sheet.DeleteRule(1);
        Assert.Equal("B) ", Texts(document));
    }
}
