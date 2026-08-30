namespace Meziantou.Framework.Sanitizers.Tests;

public class HtmlSanitizerTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("test", "test")]
    [InlineData("<p>test</p>", "<p>test</p>")]
    [InlineData("<strong>test</strong>", "<strong>test</strong>")]
    [InlineData("<p id='test'>test</p>", "<p>test</p>")]
    [InlineData("<p id='test' id='test2'>test</p>", "<p>test</p>")]
    [InlineData("<p style='color:red'>test</p>", "<p>test</p>")]
    [InlineData("<div><script></script>test</div>", "<div>test</div>")]
    [InlineData("<a href='javascript:alert(\"toto\")'>test</a>", "<a href=''>test</a>")]
    [InlineData("<a href='https://example.com'>test</a>", "<a href='https://example.com'>test</a>")]
    [InlineData("<img srcset='javascript:alert() 300w, https://example.com 600w'>", "<img srcset='' />")]
    [InlineData("<img srcset='https://example.com 300w, https://example.com 600w'>", "<img srcset='https://example.com 300w, https://example.com 600w' />")]
    public void Sanitize(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);
    }

    [Fact]
    public void Sanitize_Null_ReturnsNull()
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Null(sanitizer.SanitizeHtmlFragment(html: null));
    }

    [Theory]
    // A "<![CDATA[…]]>" section is not a CDATA section in an HTML document, it is a bogus comment that ends at
    // the first ">". Writing the section back as-is would let everything after that ">" be parsed as markup,
    // so its content is written as escaped text instead.
    [InlineData("<![CDATA[a><script>alert(1)</script>]]>", "a&gt;&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<![CDATA[a><img src=x onerror=alert(1)>]]>", "a&gt;&lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("<p>a<![CDATA[><img src=x onerror=alert(1)>]]>b</p>", "<p>a&gt;&lt;img src=x onerror=alert(1)&gt;b</p>")]
    [InlineData("<![CDATA[a & b]]>", "a &amp; b")]
    public void Sanitize_CDataSectionIsEscaped(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // Elements that are not allowed but whose content is are unwrapped: the tag is dropped, the content is kept
    [InlineData("<html><body><p>test</p></body></html>", "<p>test</p>")]
    [InlineData("<form><p>test</p></form>", "<p>test</p>")]
    [InlineData("<div><form>test</form></div>", "<div>test</div>")]
    [InlineData("<unknown-element>test</unknown-element>", "test")]
    [InlineData("<p><object data='x'><b>keep</b></object></p>", "<p><b>keep</b></p>")]
    [InlineData("<form><form><form>test</form></form></form>", "test")]
    [InlineData("<form><p>a</p>b<span>c</span></form>", "<p>a</p>b<span>c</span>")]
    // Attributes of an unwrapped element are dropped with the tag
    [InlineData("<button onclick='alert(1)'>test</button>", "test")]
    // The promoted content is sanitized too
    [InlineData("<form><script>alert(1)</script>test</form>", "test")]
    [InlineData("<form><p onclick='alert(1)'>test</p></form>", "<p>test</p>")]
    [InlineData("<form><a href='javascript:alert(1)'>test</a></form>", "<a href=''>test</a>")]
    // Processing instructions and doctypes are not real elements, they carry no content
    [InlineData("<!DOCTYPE html><p>test</p>", "<p>test</p>")]
    [InlineData("<?xml version='1.0'?><p>test</p>", "<p>test</p>")]
    [InlineData("<?php echo '<script>alert(1)</script>'; ?><p>test</p>", "<p>test</p>")]
    public void Sanitize_UnwrapsInvalidElements(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // Blocked elements are removed with their content
    [InlineData("<script>alert(1)</script>", "")]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>", "")]
    [InlineData("<style>body{color:red}</style>", "")]
    [InlineData("<p>a<script>alert(1)</script>b</p>", "<p>ab</p>")]
    // The content of a raw text element is written back verbatim, so it must never be promoted to the parent
    [InlineData("<title><img src=x onerror=alert(1)></title>", "")]
    [InlineData("<textarea><img src=x onerror=alert(1)></textarea>", "")]
    [InlineData("<xmp><img src=x onerror=alert(1)></xmp>", "")]
    [InlineData("<iframe><img src=x onerror=alert(1)></iframe>", "")]
    [InlineData("<noembed><img src=x onerror=alert(1)></noembed>", "")]
    [InlineData("<noframes><img src=x onerror=alert(1)></noframes>", "")]
    [InlineData("<noxhtml><img src=x onerror=alert(1)></noxhtml>", "")]
    [InlineData("<noscript><img src=x onerror=alert(1)></noscript>", "")]
    [InlineData("<p>a<title><img src=x onerror=alert(1)></title>b</p>", "<p>ab</p>")]
    public void Sanitize_RemovesElementsWithNonMarkupContent(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // Attributes that are not in the allow list must be removed, including namespace-qualified ones
    [InlineData("<p onclick='alert(1)'>x</p>", "<p>x</p>")]
    [InlineData("<p xxx:onclick='alert(1)'>x</p>", "<p>x</p>")]
    [InlineData("<p xml:onclick='alert(1)'>x</p>", "<p>x</p>")]
    [InlineData("<p xmlns:x='u' x:onclick='alert(1)'>y</p>", "<p>y</p>")]
    [InlineData("<p foo:class='x'>y</p>", "<p>y</p>")]
    [InlineData("<p ONCLICK='alert(1)' class='c'>x</p>", "<p class='c'>x</p>")]
    [InlineData("<p onclick='a' class='b' onclick='c'>x</p>", "<p class='b'>x</p>")]
    [InlineData("<a href=x onclick=alert(1)>x</a>", "<a href=\"x\">x</a>")]
    [InlineData("<p/onclick=alert(1)>x</p>", "<p>x</p>")]
    // xlink:href is allowed but is a URI attribute, so its value is validated
    [InlineData("<a xlink:href='javascript:alert(1)'>x</a>", "<a xlink:href=''>x</a>")]
    [InlineData("<a xlink:href='https://example.com'>x</a>", "<a xlink:href='https://example.com'>x</a>")]
    // Duplicated attributes are each sanitized on their own
    [InlineData("<a href='https://ok' href='javascript:alert(1)'>x</a>", "<a href='https://ok' href=''>x</a>")]
    [InlineData("<a href='javascript:alert(1)' href='https://ok'>x</a>", "<a href='' href='https://ok'>x</a>")]
    // Valueless attributes are kept as-is
    [InlineData("<a href>x</a>", "<a href>x</a>")]
    public void Sanitize_Attributes(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // The value of an attribute is re-written with a quote character that cannot be closed by the value itself
    [InlineData("<p title='a\" onmouseover=\"alert(1)'>x</p>", "<p title='a\" onmouseover=\"alert(1)'>x</p>")]
    [InlineData("<p title=\"a' onmouseover='alert(1)\">x</p>", "<p title=\"a' onmouseover='alert(1)\">x</p>")]
    [InlineData("<p title=a\"onmouseover=alert(1)\"x>y</p>", "<p title='a\"onmouseover=alert(1)\"x'>y</p>")]
    [InlineData("<p title=a\"b'c>x</p>", "<p title='a\"b&apos;c'>x</p>")]
    [InlineData("<p title=\"</p><script>alert(1)</script>\">x</p>", "<p title=\"</p><script>alert(1)</script>\">x</p>")]
    // Entities in an attribute value are kept encoded
    [InlineData("<p title='&lt;script&gt;'>x</p>", "<p title='&lt;script&gt;'>x</p>")]
    [InlineData("<p title=&quot;onmouseover=alert(1)&quot;>x</p>", "<p title=\"&quot;onmouseover=alert(1)&quot;\">x</p>")]
    [InlineData("<a href='http://example.com/?a=1&amp;b=2'>x</a>", "<a href='http://example.com/?a=1&amp;b=2'>x</a>")]
    public void Sanitize_AttributeValuesCannotEscapeTheirQuotes(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // Text is kept as it was written in the source, so encoded markup stays encoded
    [InlineData("&lt;script&gt;alert(1)&lt;/script&gt;", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<p>&lt;img src=x onerror=alert(1)&gt;</p>", "<p>&lt;img src=x onerror=alert(1)&gt;</p>")]
    [InlineData("<p>&#x3c;script&#x3e;alert(1)&#x3c;/script&#x3e;</p>", "<p>&#x3c;script&#x3e;alert(1)&#x3c;/script&#x3e;</p>")]
    [InlineData("a &amp; b", "a &amp; b")]
    [InlineData("a & b", "a & b")]
    [InlineData("<p>a < b</p>", "<p>a < b</p>")]
    public void Sanitize_KeepsTextAsIs(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    [InlineData("<!-- comment -->", "")]
    [InlineData("<p>a<!-- comment -->b</p>", "<p>ab</p>")]
    // Downlevel-hidden conditional comments are executed by legacy browsers
    [InlineData("<!--[if gte IE 4]><script>alert(1)</script><![endif]-->", "")]
    [InlineData("<p><!--[if gte IE 4]><script>alert(1)</script><![endif]--></p>", "<p></p>")]
    public void Sanitize_RemovesComments(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    [InlineData("<!-- comment -->", "<!-- comment -->")]
    [InlineData("<p>a<!-- comment -->b</p>", "<p>a<!-- comment -->b</p>")]
    // A comment can never be closed by its own content, so it cannot escape
    [InlineData("<p><!--a--!>b--></p>", "<p><!--a-->b--></p>")]
    public void Sanitize_AllowComments(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer { AllowComments = true };
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // Legacy and foreign-content vectors: none of these elements are allowed
    [InlineData("<svg><script>alert(1)</script></svg>", "")]
    [InlineData("<svg onload='alert(1)'></svg>", "")]
    [InlineData("<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>", "<table></table>")]
    // The end tag closes the raw text of the noscript element, exactly like a browser does, and the img that
    // follows it is a real element whose event handler is removed
    [InlineData("<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">", "<img src=\"x\" />\">")]
    [InlineData("<template><script>alert(1)</script></template>", "")]
    [InlineData("<base href='javascript:alert(1)'>", "")]
    [InlineData("<meta http-equiv='refresh' content='0;url=javascript:alert(1)'>", "")]
    [InlineData("<link rel='stylesheet' href='javascript:alert(1)'>", "")]
    [InlineData("<embed src='javascript:alert(1)'>", "")]
    [InlineData("<input onfocus='alert(1)' autofocus>", "")]
    // Everything that follows a plaintext element is raw text, so it is removed with it
    [InlineData("<p><plaintext><img src=x onerror=alert(1)>", "<p></p>")]
    [InlineData("<plaintext>a<b>c", "")]
    public void Sanitize_KnownXssVectors(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Fact]
    public void Sanitize_DeeplyNestedFragment()
    {
        const int Depth = 2_000;
        var html = string.Concat(Enumerable.Repeat("<div>", Depth)) + "test" + string.Concat(Enumerable.Repeat("</div>", Depth));

        var sanitizer = new HtmlSanitizer();
        var actual = sanitizer.SanitizeHtmlFragment(html);

        Assert.Equal(html, actual);
    }

    [Fact]
    public void Sanitize_DeeplyNestedBlockedElement()
    {
        const int Depth = 2_000;
        var html = string.Concat(Enumerable.Repeat("<div>", Depth)) + "<script>alert(1)</script>" + string.Concat(Enumerable.Repeat("</div>", Depth));
        var expected = string.Concat(Enumerable.Repeat("<div>", Depth)) + string.Concat(Enumerable.Repeat("</div>", Depth));

        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expected, sanitizer.SanitizeHtmlFragment(html));
    }

    [Fact]
    public void Sanitize_DeeplyNestedInvalidElement()
    {
        const int Depth = 2_000;
        var html = string.Concat(Enumerable.Repeat("<form>", Depth)) + "test" + string.Concat(Enumerable.Repeat("</form>", Depth));

        var sanitizer = new HtmlSanitizer();
        Assert.Equal("test", sanitizer.SanitizeHtmlFragment(html));
    }

    [Fact]
    public void Sanitize_CanAllowAdditionalElements()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("custom-element");

        Assert.Equal("<custom-element>test</custom-element>", sanitizer.SanitizeHtmlFragment("<custom-element>test</custom-element>"));
    }

    [Fact]
    public void Sanitize_CanDisallowElements()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Remove("img");

        Assert.Equal("<p>test</p>", sanitizer.SanitizeHtmlFragment("<p><img src='https://example.com'>test</p>"));
    }

    [Fact]
    public void Sanitize_CanBlockElements()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.BlockedElements.Add("p");

        Assert.Equal("<div></div>", sanitizer.SanitizeHtmlFragment("<div><p>test</p></div>"));
    }

    [Fact]
    public void Sanitize_BlockedElementsWinOverValidElements()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("script");

        Assert.Equal("", sanitizer.SanitizeHtmlFragment("<script>alert(1)</script>"));
    }

    [Fact]
    public void Sanitize_UnblockedRawTextElementIsStillNotUnwrapped()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.BlockedElements.Remove("script");

        Assert.Equal("", sanitizer.SanitizeHtmlFragment("<script>alert(1)</script>"));
    }

    [Theory]
    // The content of these elements is raw text: the parser never reads it as markup and the writer puts it back
    // exactly as it was, so allowing the element would write unsanitized markup straight through.
    [InlineData("iframe")]
    [InlineData("title")]
    [InlineData("textarea")]
    [InlineData("noscript")]
    [InlineData("noembed")]
    [InlineData("noframes")]
    [InlineData("xmp")]
    [InlineData("plaintext")]
    public void Sanitize_AllowedRawTextElementIsStillRemoved(string name)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add(name);

        Assert.Equal("", sanitizer.SanitizeHtmlFragment($"<{name}><img src=x onerror=alert(1)></{name}>"));
    }

    [Fact]
    public void Sanitize_AllowedAndUnblockedRawTextElementIsStillRemoved()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.BlockedElements.Remove("script");
        sanitizer.ValidElements.Add("script");

        Assert.Equal("<p>ab</p>", sanitizer.SanitizeHtmlFragment("<p>a<script>alert(1)</script>b</p>"));
    }

    [Fact]
    public void Sanitize_AllowedRawTextElementIsRemovedWithItsContent()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("title");

        Assert.Equal("<p>ab</p>", sanitizer.SanitizeHtmlFragment("<p>a<title><img src=x onerror=alert(1)></title>b</p>"));
    }

    [Fact]
    public void Sanitize_CanAllowAdditionalAttributes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Add("data-custom");

        Assert.Equal("<p data-custom='1'>test</p>", sanitizer.SanitizeHtmlFragment("<p data-custom='1'>test</p>"));
    }

    [Fact]
    public void Sanitize_CanDisallowAttributes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Remove("target");

        Assert.Equal("<a href='https://example.com'>test</a>", sanitizer.SanitizeHtmlFragment("<a href='https://example.com' target='_blank'>test</a>"));
    }

    [Fact]
    public void Sanitize_CanAddUriAttributes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Add("data-url");
        sanitizer.UriAttributes.Add("data-url");

        Assert.Equal("<p data-url=''>test</p>", sanitizer.SanitizeHtmlFragment("<p data-url='javascript:alert(1)'>test</p>"));
    }
}
