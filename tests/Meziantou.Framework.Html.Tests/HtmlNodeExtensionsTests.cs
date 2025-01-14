using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Html.Tests;

public class HtmlNodeExtensionsTests
{
    [Fact]
    public void Descendants()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><i><b>1</b></i>2</p>");

        var nodes = document.Descendants().ToList();
        nodes.Should().SatisfyRespectively(
            node => Assert.Equal("p", node.Name),
            node => Assert.Equal("i", node.Name),
            node => Assert.Equal("b", node.Name),
            node => Assert.Equal("1", node.Value),
            node => Assert.Equal("2", node.Value));
    }
}
