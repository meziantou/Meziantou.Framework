using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Html.Tests
{
    [TestClass]
    public class HtmlNodeExtensionsTests
    {
        [TestMethod]
        public void Descendants()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><i><b>1</b></i>2</p>");

            var nodes = document.Descendants().ToList();
            Assert.AreEqual(5, nodes.Count);
            Assert.AreEqual("p", nodes[0].Name);
            Assert.AreEqual("i", nodes[1].Name);
            Assert.AreEqual("b", nodes[2].Name);
            Assert.AreEqual("1", nodes[3].Value);
            Assert.AreEqual("2", nodes[4].Value);
        }
    }
}
