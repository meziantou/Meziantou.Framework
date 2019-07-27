using Xunit;

namespace Meziantou.Framework.Html.Tests
{
    public class EditDocumentTests
    {
        [Fact]
        public void EditDocument_ChangeTextNodeValue()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
            var node = document.SelectSingleNode("/div/p[1]/text()", HtmlNodeNavigatorOptions.LowercasedAll);
            node.Value = "edited";

            var html = document.OuterHtml;
            Assert.Equal("<div><p>edited</p><p>sample2</p></div>", html);
        }

        [Fact]
        public void EditDocument_AddElement()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
            var node = document.SelectSingleNode("/div/p[1]", HtmlNodeNavigatorOptions.LowercasedAll);
            var anchorElement = document.CreateElement("a");
            anchorElement.SetAttribute("href", "sample.txt");
            anchorElement.InnerText = "sample";
            node.AppendChild(anchorElement);

            var html = document.OuterHtml;
            Assert.Equal("<div><p>sample1<a href=\"sample.txt\">sample</a></p><p>sample2</p></div>", html);
        }
    }
}
