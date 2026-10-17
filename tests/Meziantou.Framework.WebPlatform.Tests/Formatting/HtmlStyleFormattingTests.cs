using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests.Formatting;

public sealed class HtmlStyleFormattingTests
{
    private static readonly HtmlMinifyFormatter MinifyWithCss = new() { StyleElementFormatter = new CssMinifyFormatter() };

    private static string Minify(string html) => HtmlDocumentParser.ParseDocument(html).ToHtml(MinifyWithCss);

    [Fact]
    public void NoStyleElementFormatter_WritesTheTextAsIs()
    {
        Assert.Equal("<style>a { color : red }</style>", HtmlDocumentParser.ParseDocument("<style>a { color : red }</style>").ToHtml(new HtmlMinifyFormatter()));
    }

    [Theory]
    [InlineData("<style>\n  a > b { color : #ff0000 ; }\n  /* comment */\n  c { }\n</style>", "<style>a>b{color:#f00}</style>")]
    [InlineData("<style media=print>a { color: red }</style>", "<style media=print>a{color:red}</style>")]
    [InlineData("<style type=TEXT/CSS>a { color: red }</style>", "<style type=TEXT/CSS>a{color:red}</style>")]
    [InlineData("<style></style>", "<style></style>")]
    // Not CSS: written as is
    [InlineData("<style type=text/less>a { b { c: d } }</style>", "<style type=text/less>a { b { c: d } }</style>")]
    // The SVG style elements are not raw text elements
    [InlineData("<svg><style>a { color: red }</style></svg>", "<svg><style>a { color: red }</style></svg>")]
    public void Minify_StyleElements(string html, string expected)
    {
        Assert.Equal(expected, Minify(html));
    }

    [Fact]
    public void Minify_TemplateContents()
    {
        Assert.Equal("<template><style>a{color:red}</style></template>", Minify("<template><style>a { color: red }</style></template>"));
    }

    [Fact]
    public void MultipleTextNodes_AreFormattedTogether()
    {
        var document = HtmlDocumentParser.ParseDocument("<style>a { color: red }</style>");
        var style = document.QuerySelector("style")!;
        style.AppendChild(document.CreateTextNode(" b { color: blue }"));
        Assert.Equal("<style>a{color:red}b{color:blue}</style>", style.ToHtml(MinifyWithCss));
    }

    [Fact]
    public void NonTextChildren_WritesTheTextAsIs()
    {
        var document = HtmlDocumentParser.ParseDocument("<style>a { color: red }</style>");
        var style = document.QuerySelector("style")!;
        style.AppendChild(document.CreateComment("c"));
        Assert.Equal("<style>a { color: red }<!--c--></style>", style.ToHtml(new HtmlMinifyFormatter { StyleElementFormatter = new CssMinifyFormatter(), RemoveComments = false }));
    }

    [Fact]
    public void FormattedTextThatEndsTheElement_WritesTheTextAsIs()
    {
        // The CSS escape is serialized as "<", so the formatted text would contain "</style>"
        var html = "<style>a::after { content: \"\\3c/style><script>alert(1)</script>\" }</style>";
        Assert.Equal(html, Minify(html));
    }

    [Fact]
    public void Default_WithStyleElementFormatter()
    {
        var document = HtmlDocumentParser.ParseDocument("<style>a{color:red}b{margin:0}</style>");
        var formatter = new CustomHtmlFormatter(new CssPrettyFormatter());
        Assert.Equal("<style>a {\n  color: red;\n}\nb {\n  margin: 0px;\n}</style>", document.QuerySelector("style")!.ToHtml(formatter));
    }

    [Fact]
    public void Pretty_IndentsTheStyleSheet()
    {
        var expected = """
            <html>
              <head>
                <style>
                  a {
                    color: red;
                  }
                  @media print {
                    b {}
                  }
                </style>
                <style></style>
              </head>
              <body></body>
            </html>
            """;
        var formatter = new HtmlPrettyFormatter { StyleElementFormatter = new CssPrettyFormatter() };
        Assert.Equal(expected.ReplaceLineEndings("\n"), HtmlDocumentParser.ParseDocument("<style>a{color:red}@media print{b{}}</style><style> </style>").ToHtml(formatter));
    }

    [Fact]
    public void Serialization_IsNotAffected()
    {
        var document = HtmlDocumentParser.ParseDocument("<style>a { color : red }</style>");
        Assert.Equal("<style>a { color : red }</style>", document.Head!.InnerHtml);
    }

    [Theory]
    [InlineData("<p style=\"color : red; margin: 0 auto;\">a</p>", "<p style=\"color:red;margin:0px auto\">a")]
    [InlineData("<p style='color: red'>a</p>", "<p style=color:red>a")]
    [InlineData("<p style='content: \"x\"; color: rgb(255, 0, 0)'>a</p>", "<p style='content:\"x\";color:#f00'>a")]
    // No declaration is left: the attribute is kept
    [InlineData("<p style='-unknown-property: 1'>a</p>", "<p style>a")]
    [InlineData("<svg><rect style='fill : red'/></svg>", "<svg><rect style=fill:red></rect></svg>")]
    public void Minify_StyleAttributes(string html, string expected)
    {
        var formatter = new HtmlMinifyFormatter { StyleAttributeFormatter = new CssMinifyFormatter() };
        Assert.Equal(expected, HtmlDocumentParser.ParseDocument(html).ToHtml(formatter));
    }

    [Fact]
    public void StyleAttributeFormatter_IsIndependentOfStyleElementFormatter()
    {
        const string Html = "<style>a { color : red }</style><p style='color : red'>a</p>";
        Assert.Equal("<style>a{color:red}</style><p style=\"color : red\">a", HtmlDocumentParser.ParseDocument(Html).ToHtml(MinifyWithCss));
        Assert.Equal("<style>a { color : red }</style><p style=color:red>a", HtmlDocumentParser.ParseDocument(Html).ToHtml(new HtmlMinifyFormatter { StyleAttributeFormatter = new CssMinifyFormatter() }));
    }

    [Fact]
    public void Pretty_StyleAttributes_Default_NormalizesOnASingleLine()
    {
        var formatter = new HtmlPrettyFormatter { StyleAttributeFormatter = CssFormatter.Default };
        var document = HtmlDocumentParser.ParseDocument("<p style='color:red;margin:0'>a</p>");
        Assert.Equal("<p style=\"color: red; margin: 0px;\">a</p>", document.QuerySelector("p")!.ToHtml(formatter));
    }

    [Fact]
    public void NamespacedStyleAttribute_IsNotFormatted()
    {
        var document = HtmlDocumentParser.ParseDocument("<p>a</p>");
        var p = document.QuerySelector("p")!;
        p.SetAttributeNS("urn:x", "x:style", "color : red");
        p.SetAttribute("style", "color : red");
        var formatter = new HtmlMinifyFormatter { StyleAttributeFormatter = new CssMinifyFormatter() };
        Assert.Equal("<p x:style=\"color : red\" style=color:red>a</p>", p.ToHtml(formatter));
    }

    private sealed class CustomHtmlFormatter(CssFormatter styleSheetFormatter) : HtmlFormatter
    {
        protected override HtmlMarkupWriter CreateWriter(TextWriter output) => new(output) { StyleElementFormatter = styleSheetFormatter };
    }
}
