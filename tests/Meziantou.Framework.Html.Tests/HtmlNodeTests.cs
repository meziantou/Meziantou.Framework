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
    }
}
