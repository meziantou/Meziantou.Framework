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
            node => node.Name.Should().Be("p"),
            node => node.Name.Should().Be("i"),
            node => node.Name.Should().Be("b"),
            node => node.Value.Should().Be("1"),
            node => node.Value.Should().Be("2"));
    }
}
