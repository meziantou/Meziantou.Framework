using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Html.Tests
{
    [TestClass]
    public class HtmlParserTests
    {
        [TestMethod]
        public void HtmlParser_ShouldCloseIElement()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><i>1<i>2</p>");

            var html = document.OuterHtml;
            Assert.AreEqual("<p><i>1<i>2</i></i></p>", html);
        }

        [TestMethod]
        public void HtmlParser_ShouldCloseImgElement()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><img>1<img>2</p>");

            var html = document.OuterHtml;
            Assert.AreEqual("<p><img />1<img />2</p>", html);
        }

        [TestMethod]
        public void HtmlParser_ShouldParseScriptTag()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<script type='text/javascript'>my script</script>");

            Assert.AreEqual("my script", document.SelectSingleNode("//script").InnerHtml);
            Assert.AreEqual("text/javascript", document.SelectSingleNode("//script").GetAttributeValue("type"));
        }

        [TestMethod]
        public void HtmlParser_ShouldUseBaseAddress()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
            document.BaseAddress = new System.Uri("https://www.meziantou.net");
            var absoluteUrl = document.MakeAbsoluteUrl("test.html");

            Assert.AreEqual("https://www.meziantou.net/test.html", absoluteUrl);
        }

        [TestMethod]
        public void HtmlParser_ShouldUseBaseElement()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<base href='https://www.meziantou.net'>");
            document.BaseAddress = new System.Uri("https://www.meziantou.net");
            var absoluteUrl = document.MakeAbsoluteUrl("test.html");

            Assert.AreEqual("https://www.meziantou.net/test.html", absoluteUrl);
        }

        [TestMethod]
        public void HtmlParser_ErrorTagNotOpened()
        {
            var document = new HtmlDocument();
            document.LoadHtml("</p>");

            var errors = document.Errors.ToList();

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(HtmlErrorType.TagNotOpened, errors[0].ErrorType);
        }

        [TestMethod]
        public void HtmlParser_ErrorDuplicateAttribute()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p a='a' a='b'></p>");

            var errors = document.Errors.ToList();

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(HtmlErrorType.DuplicateAttribute, errors[0].ErrorType);
        }

        [TestMethod]
        public void HtmlParser_ParseComment()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<!--Test-->");

            Assert.AreEqual(HtmlNodeType.Comment, document.FirstChild.NodeType);
        }

        [TestMethod]
        public void HtmlParser_ReadCharacterSet()
        {
            var html = "<html><head><meta charset='UTF-8'></head></html>";
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var document = new HtmlDocument();
            document.Load(memoryStream);
            Assert.AreEqual(Encoding.UTF8, document.DetectedEncoding);
        }

        [TestMethod]
        public void HtmlParser_ReadCharacterSet2()
        {
            var html = "<html><head><meta charset='UTF-7'></head></html>";
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var document = new HtmlDocument();
            document.Load(memoryStream);
            Assert.AreEqual(Encoding.UTF7, document.DetectedEncoding);
        }

        [TestMethod]
        public void HtmlParser_ReadCharacterSetFromMetaHttpEquiv()
        {
            var html = "<html><head><meta http-equiv='Content-Type' content='text/html; charset=UTF-7' /></head></html>";
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var document = new HtmlDocument();
            document.Load(memoryStream);
            Assert.AreEqual(Encoding.UTF7, document.DetectedEncoding);
        }
    }
}
