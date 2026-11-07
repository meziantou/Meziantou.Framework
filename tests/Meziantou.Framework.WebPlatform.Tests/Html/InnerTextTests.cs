using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class InnerTextTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private string GetInnerText(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions());
        var document = context.Navigate(_ => html);
        return ((HtmlElement)document.QuerySelector("#target")!).InnerText;
    }

    [Theory]
    [InlineData("<div id=target>  a   b  </div>", "a b")]
    [InlineData("<div id=target>a<div>b</div>c</div>", "a\nb\nc")]
    [InlineData("<div id=target>a<p>b</p>c</div>", "a\n\nb\n\nc")]
    [InlineData("<div id=target>a<br>b</div>", "a\nb")]
    [InlineData("<div id=target>a<span style='display:none'>b</span>c</div>", "ac")]
    [InlineData("<div id=target style='white-space:pre'>  a\n  b</div>", "  a\n  b")]
    [InlineData("<div id=target style='text-transform:uppercase'>abc</div>", "ABC")]
    [InlineData("<div id=target style='text-transform:capitalize'>foo_bar baz-qux</div>", "Foo_bar Baz-Qux")]
    [InlineData("<div>a<span id=target style='text-transform:capitalize'>b c</span></div>", "b C")]
    [InlineData("<div>a <span id=target style='text-transform:capitalize'>b c</span></div>", "B C")]
    [InlineData("<div><i>a</i><b><span id=target style='text-transform:capitalize'>b</span></b></div>", "b")]
    [InlineData("<table id=target><tr><td>a<td>b<tr><td>c<td>d</table>", "a\tb\nc\td")]
    [InlineData("<div id=target><select><option>a<option>b</select></div>", "a\nb")]
    [InlineData("<div id=target><details><summary>a</summary>b</details></div>", "a")]
    [InlineData("<div id=target><details open>b</details></div>", "b")]
    public void InnerText_RenderedText(string html, string expected) => Assert.Equal(expected, GetInnerText(html));

    [Fact]
    public void InnerText_NotRendered_ReturnsTextContent()
    {
        var document = new HtmlDocumentParser().Parse("<div id=target>  a <p>b</p></div>");
        Assert.Equal("  a b", ((HtmlElement)document.QuerySelector("#target")!).InnerText);
    }
}
