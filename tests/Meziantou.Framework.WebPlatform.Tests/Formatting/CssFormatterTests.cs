using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests.Formatting;

public sealed class CssFormatterTests
{
    internal static CssStyleSheet Parse(string css) => HtmlDocumentParser.ParseDocument("<style>" + css + "</style>").StyleSheets[0];

    [Theory]
    [InlineData("a > b, c:hover::before, d e { color: red; margin: 0 }")]
    [InlineData("a { }")]
    [InlineData("a { color: red; & .b { color: blue } color: green }")]
    [InlineData("a { .b { } }")]
    [InlineData("@media screen and (min-width: 100px), print { a { color: red } @media print { b { color: blue } } }")]
    [InlineData("@media print { }")]
    [InlineData("@supports (display: grid) { x { display: grid } }")]
    [InlineData("@container sidebar (min-width: 400px) { p { color: red } }")]
    [InlineData("@import url(a.css) layer(x) supports(display: grid) screen;")]
    [InlineData("@namespace svg url(http://www.w3.org/2000/svg);")]
    [InlineData("@font-face { font-family: \"X\" }")]
    [InlineData("@font-face { }")]
    [InlineData("@keyframes spin { from { transform: rotate(0deg) } to { transform: rotate(360deg) } }")]
    [InlineData("@page :first { margin: 1in; @top-left { } }")]
    [InlineData("@page { }")]
    [InlineData("@layer a, b;")]
    [InlineData("@layer a { p { color: red } }")]
    [InlineData("@layer { p { color: red } }")]
    [InlineData("@property --p { syntax: \"<length>\"; inherits: false; initial-value: 0px }")]
    [InlineData("@counter-style thumbs { system: cyclic; symbols: \"x\"; suffix: \" \" }")]
    [InlineData("@scope (.card) to (.content) { img { border: 1px solid black } }")]
    [InlineData("@starting-style { p { opacity: 0 } }")]
    [InlineData("a { --x: { a }; color: red !important }")]
    public void Default_IsTheCssomSerialization(string css)
    {
        var sheet = Parse(css);
        Assert.NotEmpty(sheet.CssRules);
        foreach (var rule in sheet.CssRules)
        {
            Assert.Equal(rule.CssText, rule.ToCss());
            Assert.Equal(rule.CssText, CssFormatter.Default.Format(rule));
        }

        Assert.Equal(string.Join('\n', sheet.CssRules.Select(r => r.CssText)), sheet.ToCss());
    }

    [Fact]
    public void Default_StyleDeclaration_IsTheCssText()
    {
        var document = HtmlDocumentParser.ParseDocument("<p style='color: red; margin: 0 1px; --x: y'>");
        var style = ((Meziantou.Framework.WebPlatform.Dom.HtmlElement)document.QuerySelector("p")!).Style;
        Assert.Equal("color: red; margin: 0px 1px; --x: y;", style.ToCss());
        Assert.Equal(style.CssText, style.ToCss());
    }

    [Fact]
    public void ToCss_TextWriter()
    {
        var sheet = Parse("a { color: red }");
        using var writer = new StringWriter();
        sheet.ToCss(writer, new CssMinifyFormatter());
        Assert.Equal("a{color:red}", writer.ToString());
    }

    [Fact]
    public void Format_NullArguments_Throw()
    {
        var sheet = Parse("a { color: red }");
        Assert.Throws<ArgumentNullException>(() => CssFormatter.Default.Format((CssStyleSheet)null!));
        Assert.Throws<ArgumentNullException>(() => CssFormatter.Default.Format((CssRule)null!));
        Assert.Throws<ArgumentNullException>(() => CssFormatter.Default.Format((CssStyleDeclaration)null!));
        Assert.Throws<ArgumentNullException>(() => CssFormatter.Default.Format(sheet, null!));
    }

    [Fact]
    public void CustomWriter_OverridesTheOutputOfTheDeclarations()
    {
        var sheet = Parse("a { color: red; secret-property: none; margin: 0 } @media print { .ad { display: none } } b { }");
        Assert.Equal("a { COLOR: red; MARGIN: 0px; }\nb { }", sheet.ToCss(new CustomFormatter()));
    }

    private sealed class CustomFormatter : CssFormatter
    {
        protected override CssWriter CreateWriter(TextWriter output) => new CustomWriter(output);

        private sealed class CustomWriter(TextWriter output) : CssWriter(output)
        {
            protected override bool ShouldWrite(CssRule rule) => rule is not CssMediaRule;

            protected override void WriteDeclaration(CssRule? rule, string name, string value, bool important)
            {
                if (name == "secret-property")
                    return;

                base.WriteDeclaration(rule, name.ToUpperInvariant(), value, important);
            }
        }
    }

    [Fact]
    public void Documentation_FormatStyleSheet()
    {
        #region WebPlatform_FormatCss
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("""
            /* Links */
            a > b, c:hover { color: #FF0000; margin: 0px auto !important }
            @media (min-width: 100px) { a { transition: color 0.5s } }
            """);

        var minified = sheet.ToCss(new CssMinifyFormatter());
        Assert.Equal("a>b,c:hover{color:#f00;margin:0px auto!important}@media (min-width:100px){a{transition:color .5s}}", minified);

        var indented = sheet.ToCss(new CssPrettyFormatter());
        Assert.Equal("""
            a > b,
            c:hover {
              color: rgb(255, 0, 0);
              margin: 0px auto !important;
            }
            @media (min-width: 100px) {
              a {
                transition: color 0.5s;
              }
            }
            """, indented);
        #endregion
    }
}
