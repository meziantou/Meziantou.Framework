using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests.Formatting;

public sealed class CssMinifyFormatterTests
{
    private static string Minify(string css, CssMinifyFormatter? formatter = null)
        => CssFormatterTests.Parse(css).ToCss(formatter ?? new CssMinifyFormatter());

    [Theory]
    // Selectors
    [InlineData("a > b, c ~ d, e + f, g h { color: red }", "a>b,c~d,e+f,g h{color:red}")]
    [InlineData("a :hover, a:is(b, c) * { color: red }", "a :hover,a:is(b,c) *{color:red}")]
    [InlineData("li:nth-child(2n + 1 of .a) { color: red }", "li:nth-child(2n+1 of .a){color:red}")]
    [InlineData("a[title = \"a  b\"] { color: red }", "a[title=\"a  b\"]{color:red}")]
    // Declarations
    [InlineData("a { color: red; margin: 0 auto !important; }", "a{color:red;margin:0px auto!important}")]
    [InlineData("a { font: 12px / 1.5 \"Times New Roman\", serif }", "a{font:12px/1.5 Times New Roman,serif}")]
    [InlineData("a { width: calc(1px + 2em); height: calc(2 * var(--x)) }", "a{width:calc(2em + 1px);height:calc(2*var(--x))}")]
    [InlineData("a { --x:  { a  b } ; color: var(--x,  red) }", "a{--x:{ a  b };color:var(--x,red)}")]
    [InlineData("a { background: url( 'x y.png' ) no-repeat }", "a{background:url(\"x y.png\") no-repeat}")]
    // Colors and numbers
    [InlineData("a { color: #ff0000; background-color: rgb(18, 52, 86); border-color: rgba(0, 0, 0, 0.5) }", "a{color:#f00;background-color:#123456;border-color:rgba(0,0,0,.5)}")]
    [InlineData("a { transition: opacity 0.5s, color -0.25s; opacity: 0.5 }", "a{transition:opacity .5s,color 0s -.25s;opacity:.5}")]
    // At-rules
    [InlineData("@media screen and (min-width: 100px), print { a { color: red } }", "@media screen and (min-width:100px),print{a{color:red}}")]
    [InlineData("@media (400px <= width <= 700px) { a { color: red } }", "@media (400px<=width<=700px){a{color:red}}")]
    [InlineData("@import url(a.css) screen; @layer a, b;", "@import url(\"a.css\") screen;@layer a,b;")]
    [InlineData("@keyframes k { from { opacity: 0 } 50%, to { opacity: 1 } }", "@keyframes k{0%{opacity:0}50%,100%{opacity:1}}")]
    [InlineData("@page :first { margin: 1in; @top-left { } }", "@page :first{margin:1in;@top-left{}}")]
    [InlineData("a { color: red; .b { color: blue } }", "a{color:red;& .b{color:blue}}")]
    // Empty rules
    [InlineData("a { } b { .c { } } @media print { d { } } @supports (display: grid) { } e { color: red }", "e{color:red}")]
    [InlineData("@layer a { } @keyframes k { } @font-face { }", "@layer a{}@keyframes k{}@font-face{}")]
    public void Minify_Theory(string css, string expected)
    {
        Assert.Equal(expected, Minify(css));
    }

    [Fact]
    public void Minify_Options()
    {
        var formatter = new CssMinifyFormatter { RemoveEmptyRules = false, ShortenColors = false, RemoveLeadingZeros = false };
        Assert.Equal("a{}b{color:rgb(255,0,0);opacity:0.5}", Minify("a { } b { color: red; color: #f00; opacity: 0.5 }", formatter));
    }

    [Fact]
    public void Minify_StyleDeclaration()
    {
        var document = HtmlDocumentParser.ParseDocument("<p style='color: red; margin: 0 1px'>");
        var style = ((Meziantou.Framework.WebPlatform.Dom.HtmlElement)document.QuerySelector("p")!).Style;
        Assert.Equal("color:red;margin:0px 1px", style.ToCss(new CssMinifyFormatter()));
    }

    [Fact]
    public void Minify_RoundTrips()
    {
        // The condition text of @supports and of supports() is not normalized by the CSSOM: it is not compared here
        const string Css = """
            @import url(a.css) layer(x) screen and (min-width: 100px);
            @namespace svg url(http://www.w3.org/2000/svg);
            a > b, c:hover::before, d   e, svg|rect { color: #f00; background: url(x.png) no-repeat; margin: 0px 0 0 0; width: calc(1px + 2em * 3); font: 12px/1.5 "Arial", sans-serif; transition: color 0.5s; grid-area: 1 / 2; aspect-ratio: 16 / 9; color: rgba(0, 0, 0, 0.5) !important; }
            @media screen and (min-width: 100px), print { a { color: red; .b { color: blue } color: green } }
            @keyframes spin { from { transform: rotate(0deg) } to { transform: rotate(360deg) } }
            @page :first { margin: 1in; @top-left { margin: 0 } }
            @layer a, b;
            @layer a { p { color: red } }
            @container sidebar (min-width: 400px) { p { color: red } }
            @property --p { syntax: "<length>"; inherits: false; initial-value: 0px }
            @scope (.card > a) to (.content) { img { border: 1px solid black } }
            @starting-style { p { opacity: 0 } }
            li:nth-child(2n + 1 of .x) { color: hsl(120deg 100% 50%) }
            """;
        var sheet = CssFormatterTests.Parse(Css);
        var minified = sheet.ToCss(new CssMinifyFormatter());
        Assert.Equal(sheet.ToCss(), CssFormatterTests.Parse(minified).ToCss());
    }
}
