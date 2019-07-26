using System.Linq;
using Xunit;

namespace Meziantou.Framework.Html.Tests
{
    public class HtmlNodeExtensionsTests
    {
        [Fact]
        public void Descendants()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><i><b>1</b></i>2</p>");

            var nodes = document.Descendants().ToList();
            Assert.Equal(5, nodes.Count);
            Assert.Equal("p", nodes[0].Name);
            Assert.Equal("i", nodes[1].Name);
            Assert.Equal("b", nodes[2].Name);
            Assert.Equal("1", nodes[3].Value);
            Assert.Equal("2", nodes[4].Value);
        }
    }
}
