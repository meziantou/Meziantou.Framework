using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class HtmlSerializerTests
{
    private static string RoundTrip(string html) => HtmlSerializer.SerializeChildren(HtmlDocumentParser.ParseDocument(html));

    [Theory]
    [InlineData("<!DOCTYPE html><p>a</p>", "<!DOCTYPE html><html><head></head><body><p>a</p></body></html>")]
    [InlineData("<p title='a\"b&amp;c<>'>x&amp;y&lt;z&gt;\U000000A0</p>", "<html><head></head><body><p title=\"a&quot;b&amp;c&lt;&gt;\">x&amp;y&lt;z&gt;&nbsp;</p></body></html>")]
    [InlineData("<br><img src=x><input>", "<html><head></head><body><br><img src=\"x\"><input></body></html>")]
    [InlineData("<script>a<b && c</script>", "<html><head><script>a<b && c</script></head><body></body></html>")]
    [InlineData("<style>a>b{}</style>", "<html><head><style>a>b{}</style></head><body></body></html>")]
    [InlineData("<textarea>&lt;</textarea>", "<html><head></head><body><textarea>&lt;</textarea></body></html>")]
    [InlineData("<!--c--><p><?pi data?></p>", "<!--c--><html><head></head><body><p><?pi data?></p></body></html>")]
    [InlineData("<svg><path xlink:href='x' xml:lang='en'/></svg>", "<html><head></head><body><svg><path xlink:href=\"x\" xml:lang=\"en\"></path></svg></body></html>")]
    [InlineData("<template><b>x</b></template>", "<html><head><template><b>x</b></template></head><body></body></html>")]
    [InlineData("<noscript><b>x</b></noscript>", "<html><head><noscript></noscript></head><body><b>x</b></body></html>")]
    public void Serialize(string input, string expected)
    {
        Assert.Equal(expected, RoundTrip(input));
    }

    [Fact]
    public void Serialize_DeclarativeShadowRoot()
    {
        var document = HtmlDocumentParser.ParseDocument("<div><template shadowrootmode=open shadowrootserializable><span>s</span></template><p>light</p></div>");
        var div = document.DocumentElement!.LastChild!.FirstChild!;
        Assert.Equal("<p>light</p>", HtmlSerializer.SerializeChildren(div));
        Assert.Equal("<template shadowrootmode=\"open\" shadowrootserializable=\"\"><span>s</span></template><p>light</p>", HtmlSerializer.SerializeChildren(div, serializableShadowRoots: true));
    }

    [Fact]
    public void Serialize_DeepTree_DoesNotOverflow()
    {
        const int Depth = 100_000;
        var html = string.Concat(Enumerable.Repeat("<div>", Depth));
        var document = HtmlDocumentParser.ParseDocument(html);
        var serialized = HtmlSerializer.SerializeChildren(document);
        Assert.Contains("<div><div>", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Document_GetHtml_SerializesTheWholeDocument()
    {
        var document = HtmlDocumentParser.ParseDocument("<!--before--><!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\" \"http://www.w3.org/TR/html4/strict.dtd\"><html><body><p>a</p></body></html><!--after-->");
        Assert.Equal("<!--before--><!DOCTYPE html><html><head></head><body><p>a</p></body></html><!--after-->", document.GetHtml());
    }

    [Fact]
    public void Document_WriteHtml_WritesTheSameOutputAsGetHtml()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><title>t</title><p class=x>a &amp; b</p><!--c-->");
        using var writer = new StringWriter();
        document.WriteHtml(writer);
        Assert.Equal(document.GetHtml(), writer.ToString());
        Assert.Equal("<!DOCTYPE html><html><head><title>t</title></head><body><p class=\"x\">a &amp; b</p><!--c--></body></html>", writer.ToString());
    }

    [Fact]
    public void Document_GetHtml_SerializableShadowRoots()
    {
        var document = HtmlDocumentParser.ParseDocument("<div><template shadowrootmode=open shadowrootserializable><span>s</span></template></div>");
        Assert.Equal("<html><head></head><body><div></div></body></html>", document.GetHtml());

        var expected = "<html><head></head><body><div><template shadowrootmode=\"open\" shadowrootserializable=\"\"><span>s</span></template></div></body></html>";
        Assert.Equal(expected, document.GetHtml(new GetHtmlOptions { SerializableShadowRoots = true }));

        using var writer = new StringWriter();
        document.WriteHtml(writer, new GetHtmlOptions { SerializableShadowRoots = true });
        Assert.Equal(expected, writer.ToString());
    }

    [Fact]
    public void Document_WriteHtml_NullWriter_Throws()
    {
        var document = HtmlDocumentParser.ParseDocument("<p>a</p>");
        Assert.Throws<ArgumentNullException>(() => document.WriteHtml(null!));
    }
}
