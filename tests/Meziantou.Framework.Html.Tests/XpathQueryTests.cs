using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Html.Tests
{
    [TestClass]
    public class XpathQueryTests
    {
        [TestMethod]
        public void XpathQuery01()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
            var nodes = document.SelectNodes("//p/text()", HtmlNodeNavigatorOptions.LowercasedAll).Select(node => node.Value).ToList();

            CollectionAssert.AreEqual(new[] { "sample1", "sample2" }, nodes);
        }

        [TestMethod]
        public void XPathQuery_UsingCustomContext()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p class='ABC'>Sample1</p><p class='ABCD'>Sample2</p>");
            var context = new HtmlXsltContext(document.ParentNamespaceResolver);

            var node = document.SelectSingleNode("//p[lowercase(@class)='abc']", context);
            Assert.AreEqual("Sample1", node.InnerText);
        }
    }
}
