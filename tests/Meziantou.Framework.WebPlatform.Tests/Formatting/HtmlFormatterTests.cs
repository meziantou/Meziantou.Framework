using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests.Formatting;

public sealed class HtmlFormatterTests
{
    [Theory]
    [InlineData("<!DOCTYPE html><p>a</p>")]
    [InlineData("<p title='a\"b&amp;c<>'>x&amp;y&lt;z&gt;\U000000A0</p>")]
    [InlineData("<br><img src=x><input>")]
    [InlineData("<script>a<b && c</script><style>a>b{}</style>")]
    [InlineData("<textarea>&lt;</textarea>")]
    [InlineData("<!--c--><p><?pi data?></p><!--d-->")]
    [InlineData("<svg><path xlink:href='x' xml:lang='en'/></svg>")]
    [InlineData("<template><b>x</b></template>")]
    [InlineData("<div is=my-div><span>a</span></div>")]
    public void Default_IsTheHtmlSerialization(string html)
    {
        var document = HtmlDocumentParser.ParseDocument(html);
        Assert.Equal(document.GetHtml(), document.ToHtml());
        Assert.Equal(document.GetHtml(), HtmlFormatter.Default.Format(document));
        Assert.Equal(document.DocumentElement!.OuterHtml, document.DocumentElement.ToHtml(HtmlFormatter.Default));
    }

    [Fact]
    public void ToHtml_TextWriter()
    {
        var document = HtmlDocumentParser.ParseDocument("<p>a</p>");
        using var writer = new StringWriter();
        document.ToHtml(writer, new HtmlMinifyFormatter());
        Assert.Equal("<p>a", writer.ToString());
    }

    [Fact]
    public void ToHtml_Element_IsTheOuterHtml()
    {
        var document = HtmlDocumentParser.ParseDocument("<ul><li class=a>x</li></ul>");
        var li = document.QuerySelector("li")!;
        Assert.Equal("<li class=\"a\">x</li>", li.ToHtml());
    }

    [Fact]
    public void ToHtml_DocumentFragment_WritesTheChildren()
    {
        var document = HtmlDocumentParser.ParseDocument("<template><b>x</b>y</template>");
        var template = (HtmlTemplateElement)document.QuerySelector("template")!;
        Assert.Equal("<b>x</b>y", template.Content.ToHtml());
    }

    [Fact]
    public void Format_NullArguments_Throw()
    {
        var document = HtmlDocumentParser.ParseDocument("<p>a</p>");
        Assert.Throws<ArgumentNullException>(() => HtmlFormatter.Default.Format(null!));
        Assert.Throws<ArgumentNullException>(() => HtmlFormatter.Default.Format(document, null!));
    }

    [Fact]
    public void CustomWriter_OverridesTheOutputOfANodeType()
    {
        var document = HtmlDocumentParser.ParseDocument("<p class=a>x<!--secret-->y</p>");
        Assert.Equal("<html><head></head><body><p CLASS=\"a\">xy</p></body></html>", document.ToHtml(new CustomFormatter()));
    }

    [Fact]
    public void IsVoidElement()
    {
        var document = HtmlDocumentParser.ParseDocument("<br><p></p><svg><br/></svg>");
        Assert.True(HtmlFormatter.IsVoidElement(document.QuerySelector("br")!));
        Assert.False(HtmlFormatter.IsVoidElement(document.QuerySelector("p")!));
        Assert.False(HtmlFormatter.IsVoidElement(document.CreateElementNS("http://www.w3.org/2000/svg", "br")));
    }

    [Fact]
    public void HasRawTextContent()
    {
        var document = HtmlDocumentParser.ParseDocument("<script></script><style></style><textarea></textarea><noscript></noscript>");
        Assert.True(HtmlFormatter.HasRawTextContent(document.QuerySelector("script")!));
        Assert.True(HtmlFormatter.HasRawTextContent(document.QuerySelector("style")!));
        Assert.False(HtmlFormatter.HasRawTextContent(document.QuerySelector("textarea")!));
        Assert.False(HtmlFormatter.HasRawTextContent(document.QuerySelector("noscript")!));

        var scripted = HtmlDocumentParser.ParseDocument("<noscript></noscript>", new HtmlParserOptions { ScriptingEnabled = true });
        Assert.True(HtmlFormatter.HasRawTextContent(scripted.QuerySelector("noscript")!));
    }

    [Fact]
    public void GetSerializedNames()
    {
        var document = HtmlDocumentParser.ParseDocument("<svg><use xlink:href=#a xml:lang=en xmlns:foo=urn:foo /></svg>");
        var use = document.QuerySelector("use")!;
        Assert.Equal("use", HtmlFormatter.GetSerializedTagName(use));
        Assert.Equal("xlink:href", HtmlFormatter.GetSerializedAttributeName(use.Attributes[0]!));
        Assert.Equal("xml:lang", HtmlFormatter.GetSerializedAttributeName(use.Attributes[1]!));
        Assert.Equal("xmlns:foo", HtmlFormatter.GetSerializedAttributeName(use.Attributes[2]!));
        Assert.Equal("a:b", HtmlFormatter.GetSerializedTagName(document.CreateElementNS("urn:x", "a:b")));
    }

    [Theory]
    [InlineData("a&b<c>d\"e\U000000A0", false, "a&amp;b&lt;c&gt;d\"e&nbsp;")]
    [InlineData("a&b<c>d\"e\U000000A0", true, "a&amp;b&lt;c&gt;d&quot;e&nbsp;")]
    public void WriteEscaped(string value, bool attributeMode, string expected)
    {
        using var writer = new StringWriter();
        HtmlFormatter.WriteEscaped(writer, value, attributeMode);
        Assert.Equal(expected, writer.ToString());
    }

    [Fact]
    public void DeepTree_DoesNotOverflow()
    {
        const int Depth = 100_000;
        var document = HtmlDocumentParser.ParseDocument(string.Concat(Enumerable.Repeat("<div>", Depth)) + "x");
        Assert.Contains("<div><div>", document.ToHtml(), StringComparison.Ordinal);
        Assert.Contains("<div><div>", document.ToHtml(new HtmlMinifyFormatter { CollapseWhitespace = false, TrimTrailingWhitespaceInPreformattedText = false }), StringComparison.Ordinal);
        Assert.Contains("<div>\n<div>", document.ToHtml(new HtmlPrettyFormatter { Indentation = "" }), StringComparison.Ordinal);
    }

    [Fact]
    public void DeepTree_Minify_DoesNotOverflow()
    {
        // The whitespace analysis computes the styles, whose selector matching is slow on deep trees
        const int Depth = 10_000;
        var document = HtmlDocumentParser.ParseDocument(string.Concat(Enumerable.Repeat("<span> ", Depth)) + "x");
        var minified = document.ToHtml(new HtmlMinifyFormatter());
        Assert.StartsWith("<span><span><span>", minified, StringComparison.Ordinal);
        Assert.Contains("<span>x</span></span>", minified, StringComparison.Ordinal);
    }

    private sealed class CustomFormatter : HtmlFormatter
    {
        protected override HtmlMarkupWriter CreateWriter(TextWriter output) => new CustomWriter(output);

        private sealed class CustomWriter(TextWriter output) : HtmlMarkupWriter(output)
        {
            protected override bool ShouldWrite(Node node) => node is not Comment;

            protected override void WriteAttribute(Element element, string serializedName, string value) => base.WriteAttribute(element, serializedName.ToUpperInvariant(), value);
        }
    }
}
