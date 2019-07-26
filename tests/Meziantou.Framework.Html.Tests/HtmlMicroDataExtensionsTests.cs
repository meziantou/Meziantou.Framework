using Xunit;

namespace Meziantou.Framework.Html.Tests
{
    public class HtmlMicroDataExtensionsTests
    {
        [Theory]
        [InlineData("a", "href")]
        [InlineData("area", "href")]
        [InlineData("audio", "src")]
        [InlineData("data", "value")]
        [InlineData("embed", "src")]
        [InlineData("iframe", "src")]
        [InlineData("img", "src")]
        [InlineData("link", "href")]
        [InlineData("meta", "content")]
        [InlineData("meter", "value")]
        [InlineData("object", "data")]
        [InlineData("source", "src")]
        [InlineData("time", "datetime")]
        [InlineData("track", "src")]
        [InlineData("video", "src")]
        public void GetItemValue(string tagName, string attributeName)
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<{tagName} {attributeName}='test'>");

            var value = document.FirstChild.GetItemValue();
            Assert.Equal("test", value);
        }

        [Fact]
        public void GetItemValue_Time_InnerText()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<time>test</time>");

            var value = document.FirstChild.GetItemValue();
            Assert.Equal("test", value);
        }

        [Fact]
        public void GetItemValue_Unknown_InnerText()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<dummy>test</dummy>");

            var value = document.FirstChild.GetItemValue();
            Assert.Equal("test", value);
        }

        [Fact]
        public void GetItemValue_UnsetAttribute()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<a>test</a>");

            var value = document.FirstChild.GetItemValue();
            Assert.Equal(string.Empty, value);
        }

        [Fact]
        public void GetItemScope_CurrentNode()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<a itemscope itemtype='https://schema.org/Recipe'></a>");

            var value = document.FirstChild.GetItemScopeType();
            Assert.Equal("https://schema.org/Recipe", value);
        }

        [Fact]
        public void GetItemScope_ParentNode()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<a itemscope itemtype='https://schema.org/Recipe'><img/></a>");

            var value = document.SelectSingleNode("//img").GetItemScopeType();
            Assert.Equal("https://schema.org/Recipe", value);
        }
    }
}
