using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests.Formatting;

public sealed class HtmlPrettyFormatterTests
{
    private static string Format(string html, HtmlPrettyFormatter? formatter = null)
        => HtmlDocumentParser.ParseDocument(html).ToHtml(formatter ?? new HtmlPrettyFormatter());

    [Fact]
    public void Format_IndentsTheNodes()
    {
        var expected = """
            <!DOCTYPE html>
            <html>
              <head>
                <title>Hello world</title>
                <!--c-->
              </head>
              <body>
                <div class="a">
                  <p>
                    Hello
                    <b>bold</b>
                    !
                  </p>
                  <p></p>
                  <img src="x">
                </div>
              </body>
            </html>
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), Format("<!DOCTYPE html><title> Hello \n  world </title><!--c--><div class=a>\n  <p>Hello <b>bold</b>!</p>\n  <p>  </p><img src=x></div>"));
    }

    [Fact]
    public void Format_WhitespaceSensitiveElements_AreWrittenAsIs()
    {
        var expected = """
            <html>
              <head>
                <script>if (a < b) {  }</script>
              </head>
              <body>
                <pre>  a
              <b> b </b></pre>
                <textarea>  t </textarea>
              </body>
            </html>
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), Format("<script>if (a < b) {  }</script><pre>  a\n  <b> b </b></pre><textarea>  t </textarea>"));
    }

    [Fact]
    public void Format_TemplateContents()
    {
        var expected = """
            <template>
              <span>t</span>
              <b>u</b>
            </template>
            """;
        var document = HtmlDocumentParser.ParseDocument("<template> <span>t</span> <b>u</b> </template>");
        Assert.Equal(expected.ReplaceLineEndings("\n"), document.QuerySelector("template")!.ToHtml(new HtmlPrettyFormatter()));
    }

    [Fact]
    public void Format_Options()
    {
        var formatter = new HtmlPrettyFormatter { Indentation = "\t", NewLine = "\r\n" };
        var document = HtmlDocumentParser.ParseDocument("<ul><li>a</li><li>b &amp; c</li></ul>");
        Assert.Equal("<ul>\r\n\t<li>a</li>\r\n\t<li>b &amp; c</li>\r\n</ul>", document.QuerySelector("ul")!.ToHtml(formatter));
    }

    [Fact]
    public void Format_DocumentsThatDifferByWhitespace_AreEqual()
    {
        var expected = Format("<div><p>Hello <b>world</b></p><ul><li>a</li><li>b</li></ul></div>");
        var actual = Format("<div>\n  <p>\n    Hello\n    <b> world </b>\n  </p>\n  <ul>\n    <li>a</li>\n    <li>b</li>\n  </ul>\n</div>\n");
        Assert.Equal(expected, actual);
    }
}
