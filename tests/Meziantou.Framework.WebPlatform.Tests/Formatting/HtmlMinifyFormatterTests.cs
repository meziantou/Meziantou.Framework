using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests.Formatting;

public sealed class HtmlMinifyFormatterTests
{
    private static string Minify(string html, HtmlMinifyFormatter? formatter = null, HtmlParserOptions? options = null)
        => HtmlDocumentParser.ParseDocument(html, options).ToHtml(formatter ?? new HtmlMinifyFormatter());

    [Theory]
    // Attribute quotes
    [InlineData("<!DOCTYPE html><html><head><title>a</title></head><body><div class='test'></div></body></html>", "<!DOCTYPE html><title>a</title><div class=test></div>")]
    [InlineData("<!DOCTYPE html><title>a</title><div class=\"test/\"></div>", "<!DOCTYPE html><title>a</title><div class=test/ ></div>")]
    [InlineData("<a href='/' a='b'></a>", "<a href=/ a=b></a>")]
    [InlineData("<div class=\"=\"></div>", "<div class=\"=\"></div>")]
    [InlineData("<div class=\"`\"></div>", "<div class=\"`\"></div>")]
    [InlineData("<div class=\"<\"></div>", "<div class=\"<\"></div>")]
    [InlineData("<div class=\">\"></div>", "<div class=\">\"></div>")]
    [InlineData("<div class=\"'\"></div>", "<div class=\"'\"></div>")]
    [InlineData("<div class=\"&quot;\"></div>", "<div class='\"'></div>")]
    [InlineData("<div a=\"b\" b=\"&quot;1\" c=\"&quot;'\"></div>", "<div a=b b='\"1' c=\"&quot;'\"></div>")]
    [InlineData("<div class=\"test test\"></div>", "<div class=\"test test\"></div>")]
    [InlineData("<div class=\"  a \n  b \"></div>", "<div class=\"a b\"></div>")]
    [InlineData("<script defer src='test'></script>", "<script defer src=test></script>")]
    [InlineData("<a href='?a=1&b=2&amp;c=3&#=4'>x</a>", "<a href=\"?a=1&amp;b=2&amp;c=3&amp;#=4\">x</a>")]
    [InlineData("<div><input type=text /></div>", "<div><input type=text></div>")]
    // Whitespace
    [InlineData("<div>   <i> test </i><i>test</i>  </div>", "<div><i>test </i><i>test</i></div>")]
    [InlineData("<div><i>a</i><i> b </i> </div>", "<div><i>a</i><i> b</i></div>")]
    [InlineData("<i>a </i> <i> b</i>", "<i>a </i><i>b</i>")]
    [InlineData("<span>a </span> <span> <span> b</span></span>", "<span>a </span><span><span>b</span></span>")]
    [InlineData("<span>a &nbsp; &nbsp; </span>b", "<span>a \U000000A0 \U000000A0 </span>b")]
    [InlineData("<p>a <strong>b</strong></p>", "<p>a <strong>b</strong>")]
    [InlineData("<p>a <a>b</a> <strong>c</strong></p>", "<p>a <a>b</a> <strong>c</strong>")]
    [InlineData("<p>a <a>b</a>\t<strong>c</strong></p>", "<p>a <a>b</a> <strong>c</strong>")]
    [InlineData("<p>a\n\n  b</p>", "<p>a b")]
    [InlineData("<head></head> <header><a><p class=site-title>Meziantou's blog</p></a></header> ", "<header><a><p class=site-title>Meziantou's blog</p></a></header>")]
    [InlineData("<p>a</p> <p>b</p>", "<p>a<p>b")]
    [InlineData("<head><title>a</title> \n <link rel=apple-touch-icon sizes=180x180 href=test></head>", "<title>a</title><link rel=apple-touch-icon sizes=180x180 href=test>")]
    [InlineData("<title>  a   b </title>", "<title>a b</title>")]
    [InlineData("<footer>a </footer>", "<footer>a</footer>")]
    [InlineData("<footer>a </footer><script>a</script>", "<footer>a</footer><script>a</script>")]
    [InlineData("<p>a <button>b</button> c</p>", "<p>a <button>b</button> c")]
    [InlineData("<p>a <img src=x> b</p>", "<p>a <img src=x> b")]
    [InlineData("<p>a <br> b</p>", "<p>a<br>b")]
    [InlineData("<p>a <span hidden> b </span> c</p>", "<p>a <span hidden> b </span>c")]
    [InlineData("<p>a <q>b</q> c</p>", "<p>a <q>b</q> c")]
    [InlineData("<p>a <script>x</script> b</p>", "<p>a <script>x</script>b")]
    // Preformatted text
    [InlineData("<pre>a\n b</pre>", "<pre>a\n b</pre>")]
    [InlineData("<pre>a \n b </pre>", "<pre>a\n b</pre>")]
    [InlineData("<pre>a\n <span>b</span> <span>c</span></pre>", "<pre>a\n <span>b</span> <span>c</span></pre>")]
    [InlineData("<pre><span>a </span>b </pre>", "<pre><span>a </span>b</pre>")]
    [InlineData("<pre><code>a \nb \n</code></pre>", "<pre><code>a\nb\n</code></pre>")]
    [InlineData("<p>a <span style='white-space:pre'>  b  </span> c</p>", "<p>a <span style=white-space:pre>  b  </span> c")]
    [InlineData("<textarea> a  \n b </textarea>", "<textarea> a  \n b </textarea>")]
    [InlineData("<style>pre{white-space:normal;}</style><pre>a\n b</pre>", "<style>pre{white-space:normal;}</style><pre>a b</pre>")]
    // CSS
    [InlineData("<style>a{content:\", \"}</style>", "<style>a{content:\", \"}</style>")]
    [InlineData("<style>li { display: inline; } li::after { content: ', '; }</style><ul><li>a</li> <li>b</li></ul>", "<style>li { display: inline; } li::after { content: ', '; }</style><ul><li>a<li>b</ul>")]
    [InlineData("<style>li { display: inline; }</style><ul><li>a</li> <li>b</li></ul>", "<style>li { display: inline; }</style><ul><li>a</li> <li>b</ul>")]
    [InlineData("<style>@media print { li { display: inline; } }</style><ul><li>a</li> <li>b</li></ul>", "<style>@media print { li { display: inline; } }</style><ul><li>a</li> <li>b</ul>")]
    [InlineData("<style>div{ display:inline-block } ul{ display:inline } li { display:inline }</style><div><i></i> <ul style='display:inline'><li style=display:inline>a</li> </ul>", "<style>div{ display:inline-block } ul{ display:inline } li { display:inline }</style><div><i></i><ul style=display:inline><li style=display:inline>a</ul></div>")]
    // Optional tags
    [InlineData("<img />", "<img>")]
    [InlineData("<ul><li></li><li></li></ul>", "<ul><li><li></ul>")]
    [InlineData("<a><p>test</p><p>test</p></a>", "<a><p>test<p>test</p></a>")]
    [InlineData("<my-element><p>test</p></my-element>", "<my-element><p>test</p></my-element>")]
    [InlineData("<div><p>a</p><search></search></div>", "<div><p>a<search></search></div>")]
    [InlineData("<dl><dt>a</dt><dd>b</dd><dt>c</dt><dd>d</dd></dl>", "<dl><dt>a<dd>b<dt>c<dd>d</dl>")]
    [InlineData("<table><caption>c</caption><colgroup><col></colgroup><thead><tr><th>a</th></tr></thead><tbody><tr><td>b</td><td>c</td></tr></tbody></table>", "<table><caption>c<col><thead><tr><th>a<tbody><tr><td>b<td>c</table>")]
    [InlineData("<table><tbody><tr><td>a</td></tr></tbody><tbody><tr><td>b</td></tr></tbody></table>", "<table><tr><td>a<tbody><tr><td>b</table>")]
    [InlineData("<select><optgroup label=a><option>a</option><option>b</option></optgroup></select>", "<select><optgroup label=a><option>a<option>b</select>")]
    [InlineData("<ruby>a<rt>b</rt><rp>c</rp></ruby>", "<ruby>a<rt>b<rp>c</ruby>")]
    [InlineData("<body class=a><p>x</p>", "<body class=a><p>x")]
    [InlineData("<body><script>a</script>", "<body><script>a</script>")]
    [InlineData("<body><noscript>a</noscript>", "<body><noscript>a</noscript>")]
    [InlineData("<head><meta charset=utf-8></head>", "<meta charset=utf-8>")]
    [InlineData("<template><p>a  b</p></template>", "<template><p>a b</template>")]
    // Escaping
    [InlineData("&nbsp; &amp; &amp &quot; &Agrave;&agrave &agrave", "\U000000A0 & & \" Àà à")]
    [InlineData("a &lt; b &gt; c &amp;copy; &amp;#1; d&amp;", "a &lt; b > c &amp;copy; &amp;#1; d&amp;")]
    [InlineData("<svg><use xlink:href=#a /></svg>", "<svg><use xlink:href=#a></use></svg>")]
    // Comments
    [InlineData("<p>a<!--c-->b</p>", "<p>ab")]
    public void Minify_Default(string html, string expected)
    {
        Assert.Equal(expected, Minify(html));
    }

    [Theory]
    [InlineData("<script>a</script>", "<script>a</script>")]
    [InlineData("<script type=''>a</script>", "<script>a</script>")]
    [InlineData("<script type=text/javascript>a</script>", "<script>a</script>")]
    [InlineData("<script type=' Application/ECMAScript '>a</script>", "<script>a</script>")]
    [InlineData("<script type=module>a</script>", "<script type=module>a</script>")]
    [InlineData("<script type=importmap>{}</script>", "<script type=importmap>{}</script>")]
    [InlineData("<script type=text/javascript language=vbscript>a</script>", "<script type=text/javascript language=vbscript>a</script>")]
    public void Minify_ScriptType(string html, string expected)
    {
        Assert.Equal(expected, Minify(html));
    }

    [Fact]
    public void Minify_KeepComments()
    {
        Assert.Equal("<p>a<!--c-->b", Minify("<p>a<!--c-->b</p>", new HtmlMinifyFormatter { RemoveComments = false }));
        Assert.Equal("<!--c--><p>a", Minify("<!--c--><html><p>a</p>", new HtmlMinifyFormatter { RemoveComments = false }));
        Assert.Equal("<p>a</p><!--c--><p>b", Minify("<p>a</p><!--c--><p>b</p>", new HtmlMinifyFormatter { RemoveComments = false }));
        Assert.Equal("<html><!--c--><p>a", Minify("<html><!--c--><p>a</p>", new HtmlMinifyFormatter { RemoveComments = false }));
    }

    [Fact]
    public void Minify_KeepWhitespace()
    {
        var formatter = new HtmlMinifyFormatter { CollapseWhitespace = false };
        Assert.Equal("<p class=\" a  b\"> a \n b </p>\n", Minify("<p class=' a  b'> a \n b </p>\n", formatter));
        Assert.Equal("<pre>a\n b</pre>", Minify("<pre>a \n b </pre>", formatter));
    }

    [Fact]
    public void Minify_KeepTrailingWhitespaceInPreformattedText()
    {
        Assert.Equal("<pre>a \n b </pre>", Minify("<pre>a \n b </pre>", new HtmlMinifyFormatter { TrimTrailingWhitespaceInPreformattedText = false }));
    }

    [Fact]
    public void Minify_KeepOptionalTags()
    {
        var formatter = new HtmlMinifyFormatter { OmitOptionalTags = false };
        Assert.Equal("<!DOCTYPE html><html><head><title>a</title></head><body><ul><li>a</li><li>b</li></ul></body></html>", Minify("<!DOCTYPE html><title>a</title><ul><li>a<li>b</ul>", formatter));
    }

    [Fact]
    public void Minify_PreservedStartTags()
    {
        var formatter = new HtmlMinifyFormatter { PreservedStartTags = ["HTML", "head"] };
        Assert.Equal("<!DOCTYPE html><html><head><title>a</title><script defer src=test></script>", Minify("<!DOCTYPE html><html><head><title>a</title><script defer src='test'></script></head><body></body></html>", formatter));
        Assert.Equal("<html><head><body><script>line1</script>", Minify("<body><script>line1</script>", formatter));
    }

    [Fact]
    public void Minify_KeepAttributeQuotes()
    {
        Assert.Equal("<div class=\"a\" hidden=\"\"></div>", Minify("<div class=a hidden></div>", new HtmlMinifyFormatter { RemoveAttributeQuotes = false }));
    }

    [Fact]
    public void Minify_KeepScriptType()
    {
        Assert.Equal("<script type=text/javascript>a</script>", Minify("<script type=text/javascript>a</script>", new HtmlMinifyFormatter { RemoveScriptTypeAttribute = false }));
    }

    [Fact]
    public void Minify_NoscriptWithScripting_IsRawText()
    {
        var options = new HtmlParserOptions { ScriptingEnabled = true };
        Assert.Equal("<body><noscript><b> x </b>  y &amp;</noscript>", Minify("<body><noscript><b> x </b>  y &amp;</noscript>", options: options));
    }

    [Fact]
    public void Minify_DoesNotModifyTheDocument()
    {
        var document = HtmlDocumentParser.ParseDocument("<!--c--><p>  a  <b> b </b></p><pre>x  \n</pre>");
        var before = document.GetHtml();
        _ = document.ToHtml(new HtmlMinifyFormatter());
        Assert.Equal(before, document.GetHtml());
    }

    [Fact]
    public void Minify_Element_KeepsItsTags()
    {
        var document = HtmlDocumentParser.ParseDocument("<ul><li> a  b </li></ul>");
        Assert.Equal("<li>a b</li>", document.QuerySelector("li")!.ToHtml(new HtmlMinifyFormatter()));
        Assert.Equal("<ul><li>a b</ul>", document.QuerySelector("ul")!.ToHtml(new HtmlMinifyFormatter()));
    }

    [Fact]
    public void Minify_InlineElement_KeepsTheWhitespaceAtItsEdges()
    {
        // The content around the element is unknown
        var document = HtmlDocumentParser.ParseDocument("<p>x<span> a  b </span>y</p>");
        Assert.Equal("<span> a b </span>", document.QuerySelector("span")!.ToHtml(new HtmlMinifyFormatter()));
    }

    [Fact]
    public void Minify_DocumentWithBrowsingContext()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => "<style>@media (max-width: 10px) { li { display: inline } }</style><ul><li>a</li> <li>b</li></ul>");
        Assert.Equal("<style>@media (max-width: 10px) { li { display: inline } }</style><ul><li>a</li> <li>b</ul>", document.ToHtml(new HtmlMinifyFormatter()));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><title> t </title><p>a <b>b</b> <i> c </i></p><ul><li>a</li> <li>b</li></ul><pre> x \n y </pre><table><tr><td>a</td></tr></table>")]
    [InlineData("<p>a &amp;b &lt; c <span hidden> d </span> e</p><select><option>a</option><option>b</option></select>")]
    public void Minify_IsIdempotent(string html)
    {
        var minified = Minify(html);
        Assert.Equal(minified, Minify(minified));
    }
}
