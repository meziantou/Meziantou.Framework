namespace Meziantou.Framework.Sanitizers.Tests;

public class UrlSanitizerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("HTTP://example.com")]
    [InlineData("http://example.com/?a=1&b=2")]
    [InlineData("ftp://example.com")]
    [InlineData("mailto:user@example.com")]
    [InlineData("tel:+33123456789")]
    [InlineData("file:///tmp/test.txt")]
    [InlineData("//example.com")]
    [InlineData("/path/to/page")]
    [InlineData("relative/path")]
    [InlineData("#fragment")]
    [InlineData("?query=1")]
    // A colon that appears after a '/', '?' or '#' cannot be a scheme separator
    [InlineData("#javascript:alert(1)")]
    [InlineData("?javascript:alert(1)")]
    [InlineData("/javascript:alert(1)")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("data:image/gif;base64,R0lGOD==")]
    [InlineData("data:video/mp4;base64,AAAA")]
    [InlineData("data:audio/mp3;base64,AAAA")]
    public void IsSafeUrl_Safe(string url)
    {
        Assert.True(UrlSanitizer.IsSafeUrl(url));
    }

    [Fact]
    public void IsSafeUrl_Null()
    {
        Assert.True(UrlSanitizer.IsSafeUrl(url: null));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("\tjavascript:alert(1)")]
    [InlineData("\njavascript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\0script:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    // An entity cannot be used to mask the scheme: '&' is not allowed before the first '/', '?' or '#'
    [InlineData("&#106;avascript:alert(1)")]
    [InlineData("&#x6a;avascript:alert(1)")]
    [InlineData("java&#9;script:alert(1)")]
    [InlineData("&Tab;javascript:alert(1)")]
    // Only image, video and audio data URLs are allowed
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("data:image/png,<svg onload=alert(1)>")]
    [InlineData("data:application/javascript;base64,YWxlcnQoMSk=")]
    public void IsSafeUrl_Unsafe(string url)
    {
        Assert.False(UrlSanitizer.IsSafeUrl(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(",,,")]
    [InlineData("https://example.com/a.png")]
    [InlineData("https://example.com/a.png 1x")]
    [InlineData("https://example.com/a.png 300w, https://example.com/b.png 600w")]
    [InlineData("https://example.com/a.png 1x,")]
    [InlineData("https://example.com/a.png,https://example.com/b.png")]
    [InlineData(" , https://example.com/a.png 1x , https://example.com/b.png 2x , ")]
    [InlineData("/a.png 1x, /b.png 2x")]
    // A data URL contains a comma, so the value cannot be split on ','
    [InlineData("data:image/png;base64,iVBORw0KGgo= 1x")]
    [InlineData("data:image/png;base64,iVBORw0KGgo= 1x, data:image/gif;base64,R0lGOD== 2x")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    // A comma inside a URL that is not followed by a descriptor is part of the URL
    [InlineData("a,b.png")]
    [InlineData("a,b.png 1x, c,d.png 2x")]
    public void IsSafeSrcset_Safe(string srcset)
    {
        Assert.True(UrlSanitizer.IsSafeSrcset(srcset));
    }

    [Fact]
    public void IsSafeSrcset_Null()
    {
        Assert.True(UrlSanitizer.IsSafeSrcset(url: null));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript:alert(1) 1x")]
    [InlineData(",javascript:alert(1)")]
    [InlineData(" , javascript:alert(1) 1x")]
    [InlineData("https://example.com/a.png 1x, javascript:alert(1) 2x")]
    [InlineData("javascript:alert(1) 1x, https://example.com/a.png 2x")]
    [InlineData("https://example.com/a.png 1x,javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4= 1x")]
    // "a,javascript:alert(1)" is a single relative URL for a browser, but a colon that appears before the first
    // '/', '?' or '#' makes the URL ambiguous, so it is rejected like any other URL with an unknown scheme
    [InlineData("a,javascript:alert(1)")]
    public void IsSafeSrcset_Unsafe(string srcset)
    {
        Assert.False(UrlSanitizer.IsSafeSrcset(srcset));
    }

    [Theory]
    [InlineData("<img srcset='data:image/png;base64,iVBORw0KGgo= 1x'>", "<img srcset='data:image/png;base64,iVBORw0KGgo= 1x' />")]
    [InlineData("<img srcset='https://example.com/a.png 1x, javascript:alert(1) 2x'>", "<img srcset='' />")]
    [InlineData("<img srcset=''>", "<img srcset='' />")]
    public void Sanitize_Srcset(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }
}
