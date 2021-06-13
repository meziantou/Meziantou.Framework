using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using FluentAssertions;

namespace Meziantou.Framework.Html.Tests
{
    public class HtmlParserTests
    {
        [Fact]
        public void HtmlParser_ShouldCloseIElement()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><i>1<i>2</p>");

            var html = document.OuterHtml;
            html.Should().Be("<p><i>1<i>2</i></i></p>");
        }

        [Fact]
        public void HtmlParser_ShouldCloseImgElement()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p><img>1<img>2</p>");

            var html = document.OuterHtml;
            html.Should().Be("<p><img />1<img />2</p>");
        }

        [Fact]
        public void HtmlParser_ShouldParseScriptTag()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<script type='text/javascript'>my script</script>");

            document.SelectSingleNode("//script").InnerHtml.Should().Be("my script");
            document.SelectSingleNode("//script").GetAttributeValue("type").Should().Be("text/javascript");
        }

        [Fact]
        public void HtmlParser_ShouldUseBaseAddress()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
            document.BaseAddress = new System.Uri("https://www.meziantou.net");
            var absoluteUrl = document.MakeAbsoluteUrl("test.html");

            absoluteUrl.Should().Be("https://www.meziantou.net/test.html");
        }

        [Fact]
        public void HtmlParser_ShouldUseBaseElement()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<base href='https://www.meziantou.net'>");
            document.BaseAddress = new System.Uri("https://www.meziantou.net");
            var absoluteUrl = document.MakeAbsoluteUrl("test.html");

            absoluteUrl.Should().Be("https://www.meziantou.net/test.html");
        }

        [Fact]
        public void HtmlParser_ErrorTagNotOpened()
        {
            var document = new HtmlDocument();
            document.LoadHtml("</p>");

            var errors = document.Errors.ToList();

            errors.Should().ContainSingle();
            errors[0].ErrorType.Should().Be(HtmlErrorType.TagNotOpened);
        }

        [Fact]
        public void HtmlParser_ErrorDuplicateAttribute()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<p a='a' a='b'></p>");

            var errors = document.Errors.ToList();

            errors.Should().ContainSingle();
            errors[0].ErrorType.Should().Be(HtmlErrorType.DuplicateAttribute);
        }

        [Fact]
        public void HtmlParser_ParseComment()
        {
            var document = new HtmlDocument();
            document.LoadHtml("<!--Test-->");

            document.FirstChild.NodeType.Should().Be(HtmlNodeType.Comment);
        }

        [Fact]
        public void HtmlParser_ReadCharacterSet()
        {
            var html = "<html><head><meta charset='UTF-8'></head></html>";
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var document = new HtmlDocument();
            document.Load(memoryStream);
            document.DetectedEncoding.Should().Be(Encoding.UTF8);
        }

        [Fact]
        public void HtmlParser_ReadCharacterSet2()
        {
            var html = "<html><head><meta charset='UTF-8'></head></html>";
            using var memoryStream = new MemoryStream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html));
            var document = new HtmlDocument();
            document.Load(memoryStream);
            document.DetectedEncoding.Should().Be(Encoding.UTF8);
        }

        [Fact]
        public void HtmlParser_ReadCharacterSetFromMetaHttpEquiv()
        {
            var html = "<html><head><meta http-equiv='Content-Type' content='text/html; charset=UTF-8' /></head></html>";
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var document = new HtmlDocument();
            document.Load(memoryStream);
            document.DetectedEncoding.Should().Be(Encoding.UTF8);
        }
    }
}
