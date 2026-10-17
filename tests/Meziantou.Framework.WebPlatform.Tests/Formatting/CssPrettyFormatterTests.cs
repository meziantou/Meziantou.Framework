using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests.Formatting;

public sealed class CssPrettyFormatterTests
{
    private static string Format(string css, CssPrettyFormatter? formatter = null)
        => CssFormatterTests.Parse(css).ToCss(formatter ?? new CssPrettyFormatter());

    [Fact]
    public void Format_IndentsTheRulesAndTheDeclarations()
    {
        var expected = """
            @import url("a.css") screen;
            h1,
            h2 > a {
              color: red;
              margin: 0px !important;
            }
            @media print {
              a {
                color: blue;
                & .b {
                  color: green;
                }
                padding: 0px;
              }
              b {}
            }
            @page {
              margin: 1in;
              @top-left {}
            }
            @layer a, b;
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), Format("@import 'a.css' screen; h1,h2>a{color:red;margin:0!important} @media print{a{color:blue;.b{color:green}padding:0}b{}} @page{margin:1in;@top-left{}} @layer a,b;"));
    }

    [Fact]
    public void Format_Options()
    {
        var formatter = new CssPrettyFormatter { Indentation = "\t", NewLine = "\r\n" };
        Assert.Equal("@media print {\r\n\ta {\r\n\t\tcolor: red;\r\n\t}\r\n}", Format("@media print{a{color:red}}", formatter));
    }

    [Fact]
    public void Format_StyleDeclaration()
    {
        var document = HtmlDocumentParser.ParseDocument("<p style='color: red; margin: 0'>");
        var style = ((Meziantou.Framework.WebPlatform.Dom.HtmlElement)document.QuerySelector("p")!).Style;
        Assert.Equal("color: red;\nmargin: 0px;", style.ToCss(new CssPrettyFormatter()));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        const string Css = "a>b,c{color:red;--x:{a};&:hover{color:blue}}@media (min-width:100px){@supports (display:grid){x{display:grid}}}@keyframes k{from{opacity:0}to{opacity:1}}";
        var sheet = CssFormatterTests.Parse(Css);
        Assert.Equal(sheet.ToCss(), CssFormatterTests.Parse(sheet.ToCss(new CssPrettyFormatter())).ToCss());
    }
}
