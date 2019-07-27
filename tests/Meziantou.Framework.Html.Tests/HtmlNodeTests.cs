using Xunit;

namespace Meziantou.Framework.Html.Tests
{
    public class HtmlNodeTests
    {
        [Fact]
        public void HtmlNode_InnerText()
        {
            var doc = new HtmlDocument();
            doc.AppendChild(doc.CreateText("abc"));
            Assert.Equal("abc", doc.InnerText);
        }

        [Fact]
        public void HtmlNode_InnerText_CombineValues()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("abc<p>def</p>");
            Assert.Equal("abcdef", doc.InnerText);
        }

        [Fact]
        public void HtmlNode_ParentElement_Null()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<p>def</p>");
            Assert.Null(doc.SelectSingleNode("/p").ParentElement);
        }

        [Fact]
        public void HtmlNode_ParentElement()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<p>def</p>");
            Assert.Equal("p", doc.SelectSingleNode("/p/node()").ParentElement.Name);
        }
    }
}
