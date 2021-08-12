using System.Linq;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Html.Tests;

public class XpathQueryTests
{
    [Fact]
    public void XpathQuery01()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
        var nodes = document.SelectNodes("//p/text()", HtmlNodeNavigatorOptions.LowercasedAll).Select(node => node.Value).ToList();

        nodes.Should().Equal(new[] { "sample1", "sample2" });
    }

    [Fact]
    public void XPathQuery_UsingCustomContext()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p class='ABC'>Sample1</p><p class='ABCD'>Sample2</p>");
        var context = new HtmlXsltContext(document.ParentNamespaceResolver);

        var node = document.SelectSingleNode("//p[lowercase(@class)='abc']", context);
        node.InnerText.Should().Be("Sample1");
    }
}
