using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Html.Tests
{
    [TestClass]
    public class HtmlMicroDataExtensionsTests
    {
        [DataTestMethod]
        [DataRow("a", "href")]
        [DataRow("area", "href")]
        [DataRow("audio", "src")]
        [DataRow("data", "value")]
        [DataRow("embed", "src")]
        [DataRow("iframe", "src")]
        [DataRow("img", "src")]
        [DataRow("link", "href")]
        [DataRow("meta", "content")]
        [DataRow("meter", "value")]
        [DataRow("object", "data")]
        [DataRow("source", "src")]
        [DataRow("time", "datetime")]
        [DataRow("track", "src")]
        [DataRow("video", "src")]
        public void GetItemValue(string tagName, string attributeName)
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<{tagName} {attributeName}='test'>");

            var value = document.FirstChild.GetItemValue();
            Assert.AreEqual("test", value);
        }

        [TestMethod()]
        public void GetItemValue_Time_InnerText()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<time>test</time>");

            var value = document.FirstChild.GetItemValue();
            Assert.AreEqual("test", value);
        }

        [TestMethod()]
        public void GetItemValue_Unknown_InnerText()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<dummy>test</dummy>");

            var value = document.FirstChild.GetItemValue();
            Assert.AreEqual("test", value);
        }

        [TestMethod()]
        public void GetItemValue_UnsetAttribute()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<a>test</a>");

            var value = document.FirstChild.GetItemValue();
            Assert.AreEqual(string.Empty, value);
        }

        [TestMethod()]
        public void GetItemScope_CurrentNode()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<a itemscope itemtype='https://schema.org/Recipe'></a>");

            var value = document.FirstChild.GetItemScopeType();
            Assert.AreEqual("https://schema.org/Recipe", value);
        }

        [TestMethod()]
        public void GetItemScope_ParentNode()
        {
            var document = new HtmlDocument();
            document.LoadHtml($"<a itemscope itemtype='https://schema.org/Recipe'><img/></a>");

            var value = document.SelectSingleNode("//img").GetItemScopeType();
            Assert.AreEqual("https://schema.org/Recipe", value);
        }
    }
}
