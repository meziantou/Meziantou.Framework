using Xunit;
using FluentAssertions;

namespace Meziantou.Framework.Html.Tests
{
    public class HtmlNodeTests
    {
        [Fact]
        public void HtmlNode_InnerText()
        {
            var doc = new HtmlDocument();
            doc.AppendChild(doc.CreateText("abc"));
            doc.InnerText.Should().Be("abc");
        }

        [Fact]
        public void HtmlNode_InnerText_CombineValues()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("abc<p>def</p>");
            doc.InnerText.Should().Be("abcdef");
        }

        [Fact]
        public void HtmlNode_ParentElement_Null()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<p>def</p>");
            doc.SelectSingleNode("/p").ParentElement.Should().BeNull();
        }

        [Fact]
        public void HtmlNode_ParentElement()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<p>def</p>");
            doc.SelectSingleNode("/p/node()").ParentElement.Name.Should().Be("p");
        }
    }
}
