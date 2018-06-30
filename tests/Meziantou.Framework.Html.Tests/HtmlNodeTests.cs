using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Html.Tests
{
    [TestClass]
    public class HtmlNodeTests
    {
        [TestMethod]
        public void HtmlNode_InnerText()
        {
            var doc = new HtmlDocument();
            doc.AppendChild(doc.CreateText("abc"));
            Assert.AreEqual("abc", doc.InnerText);
        }

        [TestMethod]
        public void HtmlNode_InnerText_CombineValues()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("abc<p>def</p>");
            Assert.AreEqual("abcdef", doc.InnerText);
        }

        [TestMethod]
        public void HtmlNode_ParentElement_Null()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<p>def</p>");
            Assert.AreEqual(null, doc.SelectSingleNode("/p").ParentElement);
        }

        [TestMethod]
        public void HtmlNode_ParentElement()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<p>def</p>");
            Assert.AreEqual("p", doc.SelectSingleNode("/p/node()").ParentElement.Name);
        }
    }
}
