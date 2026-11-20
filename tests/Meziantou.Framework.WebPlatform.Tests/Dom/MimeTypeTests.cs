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

    // The valid MIME type strings of Media Capabilities (Media.MediaContentType): a strict parser
    [Theory]
    [InlineData("video/webm; codecs=\"vp8\"", "video/webm;codecs=vp8")]
    [InlineData(" Video/WebM;CODECS=\"vp09.00.10.08, opus\" ", "video/webm;codecs=\"vp09.00.10.08, opus\"")]
    [InlineData("video/VP9; profile-level=\"0\"", "video/vp9;profile-level=0")]
    [InlineData("audio/mp3", "audio/mp3")]
    [InlineData("video/webm; codecs=\"a\\\"b\"; foo=bar", "video/webm;codecs=\"a\\\"b\";foo=bar")]
    [InlineData("video/webm; codecs=\"\"", "video/webm;codecs=\"\"")]
    public void ParseContentType(string input, string expected)
    {
        Assert.Equal(expected, Media.MediaContentType.Parse(input)?.ToString());
    }

    [Theory]
    [InlineData("fgeoa")]
    [InlineData("video/webm;")]
    [InlineData("audio/mpeg;")]
    [InlineData("video/VP9\"")]
    [InlineData("video/webm; codecs")]
    [InlineData("video/webm; codecs=")]
    [InlineData("video/webm; codecs=\"vp8")]
    [InlineData("video/webm; codecs=a b")]
    [InlineData("video /webm")]
    public void ParseContentType_Failure(string input)
    {
        Assert.Null(Media.MediaContentType.Parse(input));
    }
}
