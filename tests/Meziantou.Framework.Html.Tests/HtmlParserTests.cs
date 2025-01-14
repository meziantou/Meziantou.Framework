using System.Text;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Html.Tests;

public class HtmlParserTests
{
    [Fact]
    public void HtmlParser_ShouldCloseIElement()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><i>1<i>2</p>");

        var html = document.OuterHtml;
        Assert.Equal("<p><i>1<i>2</i></i></p>", html);
    }

    [Fact]
    public void HtmlParser_ShouldCloseImgElement()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><img>1<img>2</p>");

        var html = document.OuterHtml;
        Assert.Equal("<p><img />1<img />2</p>", html);
    }

    [Fact]
    public void HtmlParser_ShouldParseScriptTag()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<script type='text/javascript'>my script</script>");
        Assert.Equal("my script", document.SelectSingleNode("//script").InnerHtml);
        Assert.Equal("text/javascript", document.SelectSingleNode("//script").GetAttributeValue("type"));
    }

    [Fact]
    public void HtmlParser_ShouldUseBaseAddress()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
        document.BaseAddress = new System.Uri("https://www.meziantou.net");
        var absoluteUrl = document.MakeAbsoluteUrl("test.html");
        Assert.Equal("https://www.meziantou.net/test.html", absoluteUrl);
    }

    [Fact]
    public void HtmlParser_ShouldUseBaseElement()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<base href='https://www.meziantou.net'>");
        document.BaseAddress = new System.Uri("https://www.meziantou.net");
        var absoluteUrl = document.MakeAbsoluteUrl("test.html");
        Assert.Equal("https://www.meziantou.net/test.html", absoluteUrl);
    }

    [Fact]
    public void HtmlParser_ErrorTagNotOpened()
    {
        var document = new HtmlDocument();
        document.LoadHtml("</p>");

        var errors = document.Errors.ToList();

        errors.Should().ContainSingle();
        Assert.Equal(HtmlErrorType.TagNotOpened, errors[0].ErrorType);
    }

    [Fact]
    public void HtmlParser_ErrorDuplicateAttribute()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p a='a' a='b'></p>");

        var errors = document.Errors.ToList();

        errors.Should().ContainSingle();
        Assert.Equal(HtmlErrorType.DuplicateAttribute, errors[0].ErrorType);
    }

    [Fact]
    public void HtmlParser_ParseComment()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<!--Test-->");
        Assert.Equal(HtmlNodeType.Comment, document.FirstChild.NodeType);
    }

    [Fact]
    public void HtmlParser_ReadCharacterSet()
    {
        var html = "<html><head><meta charset='UTF-8'></head></html>";
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var document = new HtmlDocument();
        document.Load(memoryStream);
        Assert.Equal(Encoding.UTF8, document.DetectedEncoding);
    }

    [Fact]
    public void HtmlParser_ReadCharacterSet2()
    {
        var html = "<html><head><meta charset='UTF-8'></head></html>";
        using var memoryStream = new MemoryStream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html));
        var document = new HtmlDocument();
        document.Load(memoryStream);
        Assert.Equal(Encoding.UTF8, document.DetectedEncoding);
    }

    [Fact]
    public void HtmlParser_ReadCharacterSetFromMetaHttpEquiv()
    {
        var html = "<html><head><meta http-equiv='Content-Type' content='text/html; charset=UTF-8' /></head></html>";
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var document = new HtmlDocument();
        document.Load(memoryStream);
        Assert.Equal(Encoding.UTF8, document.DetectedEncoding);
    }
}
