using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.XPath;

namespace Meziantou.Framework.WebPlatform.AotTests;

/// <summary>
/// Exercises the main features of the libraries in a Native AOT, trimmed application: the features that only break once
/// trimmed (reflection, missing metadata) fail the tests.
/// </summary>
public sealed class WebPlatformAotTests
{
    private const string Html = """
        <!DOCTYPE html>
        <style>.important { color: red; font-weight: bold } li:nth-child(2n) { margin-left: calc(1em + 2px) }</style>
        <ul>
          <li><a href="https://example.com/a" class="important">A</a>
          <li><a href="/b" style="color: blue">B</a>
        </ul>
        <table><tr><td>cell</table>
        """;

    [Fact]
    public void ParseAndQuery()
    {
        var document = HtmlDocumentParser.ParseDocument(Html);
        Assert.Equal("<table><tbody><tr><td>cell</td></tr></tbody></table>", document.QuerySelector("table")?.OuterHtml);
        Assert.Equal("A,B", string.Join(',', document.QuerySelectorAll<HtmlAnchorElement>("li > a").Select(a => a.TextContent)));

        var xpath = document.Evaluate("//a[@class='important']", document, type: XPathResult.FirstOrderedNodeType);
        Assert.Equal("https://example.com/a", ((Element?)xpath.SingleNodeValue)?.GetAttribute("href"));
        Assert.Equal("blue", document.QuerySelector<HtmlElement>("a[href='/b']")?.Style.GetPropertyValue("color"));
        Assert.Equal(7, document.QuerySelector("table")?.ToHtml(new HtmlPrettyFormatter()).Split('\n').Length);
    }

    [Fact]
    public void MutationsAndSanitizer()
    {
        var document = HtmlDocumentParser.ParseDocument(Html);
        var list = document.QuerySelector("ul")!;
        list.InsertAdjacentHtml("beforeend", "<li><a href=/c>C</a>");
        Assert.Equal(3u, list.Children.Length);

        var div = document.CreateElement("div");
        div.SetHtml("<b onclick='alert(1)'>bold</b><script>alert(2)</script>");
        Assert.Equal("<b>bold</b>", div.InnerHtml);
    }

    [Fact]
    public void Xml()
    {
        var xml = XmlDocumentParser.ParseDocument("<root xmlns:x='urn:x'><x:item id='1'/><item>text</item></root>");
        Assert.Equal("urn:x", xml.DocumentElement?.Children[0].NamespaceUri);
    }

    [Fact]
    public void ParseUrl()
    {
        Assert.Equal("https://example.com/c?q=1#f", Url.Parse("../c?q=1#f", "https://example.com/a/b")?.Href);
    }

    [Fact]
    public void Cssom()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("a { color: rgb(0 128 0 / 50%) } @media (min-width: 100px) { b { margin: 1px 2px } }");
        Assert.Equal("a { color: rgba(0, 128, 0, 0.5); }", sheet.CssRules[0].CssText);
    }

    [Fact]
    public async Task BrowsingContextWithScripts()
    {
        using var engine = new JintScriptEngine(new JintScriptEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
        var page = await context.NavigateAsync("https://example.com/", _ => Html + """
            <script>
              document.querySelector('ul').insertAdjacentHTML('beforeend', '<li id=from-script>' + [1, 2, 3].map(x => x * 2).join('-'));
              setTimeout(() => document.title = 'timer', 10);
            </script>
            """);
        do
        {
            await context.EventLoop.RunUntilIdleAsync();
        }
        while (await context.EventLoop.WaitForNextTimerAsync());

        var style = context.Window.GetComputedStyle(page.QuerySelector(".important")!);
        Assert.Equal("rgb(255, 0, 0)", style.Color);
        Assert.Equal("700", style.GetPropertyValue("font-weight"));
        Assert.Equal("18px", context.Window.GetComputedStyle(page.QuerySelectorAll<Element>("li")[1]).GetPropertyValue("margin-left"));
        Assert.Equal("2-4-6", page.GetElementById("from-script")?.TextContent);
        Assert.Equal("timer", page.Title);
        Assert.Equal("function:https://example.com/x", Convert.ToString(engine.Evaluate(context.Window, "typeof document.createElement('canvas').getContext + ':' + new URL('x', location.href).href"), CultureInfo.InvariantCulture));
    }
}
