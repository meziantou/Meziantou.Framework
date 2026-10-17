using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://mimesniff.spec.whatwg.org/#parsing-a-mime-type</summary>
public sealed class MimeTypeTests
{
    [Theory]
    [InlineData("text/html", "text/html")]
    [InlineData(" Text/HTML ;Charset=UTF-8 ", "text/html;charset=UTF-8")]
    [InlineData("text/html;charset=\"utf-8\"", "text/html;charset=utf-8")]
    [InlineData("text/html;charset=\"a\\\"b\";charset=c", "text/html;charset=\"a\\\"b\"")]
    [InlineData("text/html;;x=1;y;z=", "text/html;x=1")]
    [InlineData("text/html;x=\"\"", "text/html;x=\"\"")]
    [InlineData("text/html;x=a b", "text/html;x=\"a b\"")]
    [InlineData("text/html;x=\"unterminated", "text/html;x=unterminated")]
    public void Parse(string input, string expected)
    {
        Assert.Equal(expected, MimeType.Parse(input)?.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("text")]
    [InlineData("text/")]
    [InlineData("/html")]
    [InlineData("te xt/html")]
    [InlineData("text/ht(ml")]
    public void Parse_Failure(string input)
    {
        Assert.Null(MimeType.Parse(input));
    }
}
