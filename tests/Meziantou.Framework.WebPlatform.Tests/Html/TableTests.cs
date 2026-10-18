using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The table APIs (https://html.spec.whatwg.org/multipage/tables.html).</summary>
public sealed class TableTests
{
    private static HtmlTableElement Parse(string html) => (HtmlTableElement)new HtmlDocumentParser().Parse(html).QuerySelector("table")!;

    [Fact]
    public void DeleteRow_MinusOne_RemovesTheLastRow()
    {
        var table = Parse("<table><tr id=a><tr id=b></table>");
        table.DeleteRow(-1);
        Assert.Equal(["a"], table.Rows.Select(r => r.Id));
    }

    [Fact]
    public void DeleteRow_MinusOneWithoutRows_DoesNothing()
    {
        var table = Parse("<table></table>");
        table.DeleteRow(-1);
        Assert.Empty(table.Rows);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => table.DeleteRow(0)).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => table.DeleteRow(-2)).Name);
    }
}
