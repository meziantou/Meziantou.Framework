using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Html.Tests
{
    [TestClass]
    public class HtmlNodeDepthComparerTests
    {
        [TestMethod]
        public void Compare_Equals()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><span id='id1'>1</span><span id='id2'>2</span></p>");
            var element1 = document.SelectSingleNode("//span[@id='id1']");
            var element2 = document.SelectSingleNode("//span[@id='id2']");

            var comparer = new HtmlNodeDepthComparer();
            Assert.AreEqual(0, comparer.Compare(element1, element2));
        }

        [TestMethod]
        public void Compare_DirectionAscending_LessThan()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><span id='id1'>1</span><p><span id='id2'>2</span></p></p>");
            var element1 = document.SelectSingleNode("//span[@id='id1']");
            var element2 = document.SelectSingleNode("//span[@id='id2']");

            var comparer = new HtmlNodeDepthComparer();
            comparer.Direction = ListSortDirection.Ascending;
            Assert.AreEqual(-1, comparer.Compare(element1, element2));
            Assert.AreEqual(1, comparer.Compare(element2, element1));
        }

        [TestMethod]
        public void Compare_DirectionDescending_LessThan()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><span id='id1'>1</span><p><span id='id2'>2</span></p></p>");
            var element1 = document.SelectSingleNode("//span[@id='id1']");
            var element2 = document.SelectSingleNode("//span[@id='id2']");

            var comparer = new HtmlNodeDepthComparer();
            comparer.Direction = ListSortDirection.Descending;
            Assert.AreEqual(1, comparer.Compare(element1, element2));
            Assert.AreEqual(-1, comparer.Compare(element2, element1));
        }
    }
}
