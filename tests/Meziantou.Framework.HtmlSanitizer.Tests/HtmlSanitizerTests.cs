using System.Text;

namespace Meziantou.Framework.Sanitizers.Tests;

public class HtmlSanitizerTests
{
    [Fact]
    public void ReadmeExample_Basic()
    {
        #region HtmlSanitizer_Basic
        var sanitizer = new HtmlSanitizer();
        var safeHtml = sanitizer.SanitizeHtmlFragment("<p>Hello <script>alert('xss')</script>World</p>");
        Assert.Equal("<p>Hello World</p>", safeHtml);
        #endregion
    }

    [Fact]
    public void ReadmeExample_RemoveAttributes()
    {
        #region HtmlSanitizer_RemoveAttributes
        var sanitizer = new HtmlSanitizer();

        // Removes style attributes
        var result1 = sanitizer.SanitizeHtmlFragment("<p style='color:red'>test</p>");
        Assert.Equal("<p>test</p>", result1);

        // Removes id attributes (not in allowed list)
        var result2 = sanitizer.SanitizeHtmlFragment("<p id='test'>test</p>");
        Assert.Equal("<p>test</p>", result2);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Urls()
    {
        #region HtmlSanitizer_Urls
        var sanitizer = new HtmlSanitizer();

        // Blocks javascript: URLs
        var result1 = sanitizer.SanitizeHtmlFragment("<a href='javascript:alert(\"xss\")'>click</a>");
        Assert.Equal("<a href=\"\">click</a>", result1);

        // Allows safe URLs
        var result2 = sanitizer.SanitizeHtmlFragment("<a href='https://example.com'>click</a>");
        Assert.Equal("<a href=\"https://example.com\">click</a>", result2);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Srcset()
    {
        #region HtmlSanitizer_Srcset
        var sanitizer = new HtmlSanitizer();

        // Blocks srcset with dangerous URLs
        var result1 = sanitizer.SanitizeHtmlFragment("<img srcset='javascript:alert() 300w, https://example.com 600w'>");
        Assert.Equal("<img srcset=\"\">", result1);

        // Allows safe srcset
        var result2 = sanitizer.SanitizeHtmlFragment("<img srcset='https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w'>");
        Assert.Equal("<img srcset=\"https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w\">", result2);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Text()
    {
        #region HtmlSanitizer_Text
        var sanitizer = new HtmlSanitizer();

        Assert.Equal("<p>&lt;script&gt;</p>", sanitizer.SanitizeHtmlFragment("<p>&lt;script&gt;</p>"));
        Assert.Equal("<p>a &lt; b &amp; c</p>", sanitizer.SanitizeHtmlFragment("<p>a < b & c</p>"));
        Assert.Equal("<b></b>", sanitizer.SanitizeHtmlFragment("<b><!--"));
        #endregion
    }

    [Fact]
    public void ReadmeExample_AllowComments()
    {
        #region HtmlSanitizer_AllowComments
        var sanitizer = new HtmlSanitizer { AllowComments = true };
        var result = sanitizer.SanitizeHtmlFragment("<p>a<!-- comment -->b</p>");
        Assert.Equal("<p>a<!-- comment -->b</p>", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_CustomElements()
    {
        #region HtmlSanitizer_CustomElements
        var sanitizer = new HtmlSanitizer();

        // Add custom elements
        sanitizer.ValidElements.Add("custom-element");

        // Has no effect: the content of a raw text element is not markup, so the element is
        // removed with its content whether or not it is in ValidElements
        sanitizer.ValidElements.Add("iframe");

        // Remove elements from allowed list
        sanitizer.ValidElements.Remove("img");

        // Add elements to blocked list (will be removed entirely)
        sanitizer.BlockedElements.Add("iframe");

        var result = sanitizer.SanitizeHtmlFragment("<custom-element>a</custom-element><iframe>b</iframe><img src='c.png'>");
        Assert.Equal("<custom-element>a</custom-element>", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_CustomAttributes()
    {
        #region HtmlSanitizer_CustomAttributes
        var sanitizer = new HtmlSanitizer();

        // Add custom attributes
        sanitizer.ValidAttributes.Add("data-custom");

        // Remove attributes from allowed list
        sanitizer.ValidAttributes.Remove("target");

        // Add attributes that should be URL-validated
        sanitizer.UriAttributes.Add("data-url");

        var result = sanitizer.SanitizeHtmlFragment("<a data-custom='value' target='_blank' href='https://example.com'>link</a>");
        Assert.Equal("<a data-custom=\"value\" href=\"https://example.com\">link</a>", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_AttributePrefixes()
    {
        #region HtmlSanitizer_AttributePrefixes
        var sanitizer = new HtmlSanitizer();

        // Allow every data-* attribute
        sanitizer.ValidAttributePrefixes.Add("data-");

        // Remove the ARIA attributes
        sanitizer.ValidAttributePrefixes.Remove("aria-");

        var result = sanitizer.SanitizeHtmlFragment("<p data-id='1' aria-label='text'>test</p>");
        Assert.Equal("<p data-id=\"1\">test</p>", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_UriAttributes()
    {
        #region HtmlSanitizer_UriAttributes
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Add("data-url");

        // The value is not validated
        Assert.Equal("<p data-url=\"javascript:alert(1)\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-url='javascript:alert(1)'>test</p>"));

        sanitizer.UriAttributes.Add("data-url");
        Assert.Equal("<p data-url=\"\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-url='javascript:alert(1)'>test</p>"));
        #endregion
    }

    [Fact]
    public void ReadmeExample_ElementHandling()
    {
        #region HtmlSanitizer_ElementHandling
        var sanitizer = new HtmlSanitizer();

        // script is blocked, so its content is removed too
        Assert.Equal("<p>Hello World</p>", sanitizer.SanitizeHtmlFragment("<p>Hello <script>alert('xss')</script>World</p>"));

        // form is not allowed, but its content is kept
        Assert.Equal("<p>Hello</p>", sanitizer.SanitizeHtmlFragment("<form><p>Hello</p></form>"));
        #endregion
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("test", "test")]
    [InlineData("<p>test</p>", "<p>test</p>")]
    [InlineData("<strong>test</strong>", "<strong>test</strong>")]
    [InlineData("<p id='test'>test</p>", "<p>test</p>")]
    [InlineData("<p id='test' id='test2'>test</p>", "<p>test</p>")]
    [InlineData("<p style='color:red'>test</p>", "<p>test</p>")]
    [InlineData("<div><script></script>test</div>", "<div>test</div>")]
    [InlineData("<a href='javascript:alert(\"toto\")'>test</a>", "<a href=\"\">test</a>")]
    [InlineData("<a href='https://example.com'>test</a>", "<a href=\"https://example.com\">test</a>")]
    [InlineData("<img srcset='javascript:alert() 300w, https://example.com 600w'>", "<img srcset=\"\">")]
    [InlineData("<img srcset='https://example.com 300w, https://example.com 600w'>", "<img srcset=\"https://example.com 300w, https://example.com 600w\">")]
    public void Sanitize(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);
    }

    [Fact]
    public void Sanitize_DocumentationExample()
    {
        #region HtmlSanitizer_SanitizeHtmlFragment
        var sanitizer = new HtmlSanitizer();
        var safeHtml = sanitizer.SanitizeHtmlFragment("<p>Hello <script>alert('xss')</script>World</p>");
        Assert.Equal("<p>Hello World</p>", safeHtml);
        #endregion
    }

    [Fact]
    public void Sanitize_Null_ReturnsNull()
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Null(sanitizer.SanitizeHtmlFragment(html: null));
    }

    [Theory]
    // A "<![CDATA[…]]>" section is not a CDATA section in an HTML document, it is a bogus comment that ends at
    // the first ">". Everything after that ">" is markup, exactly as a browser reads it, so it is sanitized.
    [InlineData("<![CDATA[a><script>alert(1)</script>]]>", "]]&gt;")]
    [InlineData("<![CDATA[a><img src=x onerror=alert(1)>]]>", "<img src=\"x\">]]&gt;")]
    [InlineData("<p>a<![CDATA[><img src=x onerror=alert(1)>]]>b</p>", "<p>a<img src=\"x\">]]&gt;b</p>")]
    [InlineData("<![CDATA[a & b]]>", "")]
    public void Sanitize_CDataSectionIsABogusComment(string html, string expectedResult)
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
    [InlineData("<form><a href='javascript:alert(1)'>test</a></form>", "<a href=\"\">test</a>")]
    [InlineData("<noxhtml><img src=x onerror=alert(1)></noxhtml>", "<img src=\"x\">")]
    // The content of a template element is promoted too
    [InlineData("<template><p onclick='alert(1)'>test</p></template>", "<p>test</p>")]
    // Doctypes are ignored in a fragment. Processing instructions are bogus comments that end at the first ">", and
    // what follows is sanitized as any other markup.
    [InlineData("<!DOCTYPE html><p>test</p>", "<p>test</p>")]
    [InlineData("<?xml version='1.0'?><p>test</p>", "<p>test</p>")]
    [InlineData("<?php echo '<script>alert(1)</script>'; ?><p>test</p>", "alert(1)'; ?&gt;<p>test</p>")]
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
    [InlineData("<p ONCLICK='alert(1)' class='c'>x</p>", "<p class=\"c\">x</p>")]
    [InlineData("<p onclick='a' class='b' onclick='c'>x</p>", "<p class=\"b\">x</p>")]
    [InlineData("<a href=x onclick=alert(1)>x</a>", "<a href=\"x\">x</a>")]
    [InlineData("<p/onclick=alert(1)>x</p>", "<p>x</p>")]
    // xlink:href is allowed but is a URI attribute, so its value is validated
    [InlineData("<a xlink:href='javascript:alert(1)'>x</a>", "<a xlink:href=\"\">x</a>")]
    [InlineData("<a xlink:href='https://example.com'>x</a>", "<a xlink:href=\"https://example.com\">x</a>")]
    // As in a browser, the first of duplicated attributes wins
    [InlineData("<a href='https://ok' href='javascript:alert(1)'>x</a>", "<a href=\"https://ok\">x</a>")]
    [InlineData("<a href='javascript:alert(1)' href='https://ok'>x</a>", "<a href=\"\">x</a>")]
    // Valueless attributes have an empty value
    [InlineData("<a href>x</a>", "<a href=\"\">x</a>")]
    // ARIA attributes are allowed by their prefix
    [InlineData("<p role='note' aria-label='a' ARIA-HIDDEN='true'>x</p>", "<p role=\"note\" aria-label=\"a\" aria-hidden=\"true\">x</p>")]
    [InlineData("<p arialabel='a' x-aria-label='b'>x</p>", "<p>x</p>")]
    // The "is" value of a customized built-in element is written back as an attribute unless the element is recreated
    [InlineData("<p is='x-foo' class='a'>x</p>", "<p class=\"a\">x</p>")]
    [InlineData("<p is>x<b is=x-foo>y</b></p>", "<p>x<b>y</b></p>")]
    [InlineData("<p hidden translate='no'><time datetime='2026-01-01'>x</time></p>", "<p hidden=\"\" translate=\"no\"><time datetime=\"2026-01-01\">x</time></p>")]
    public void Sanitize_Attributes(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // The value of an attribute is written between double quotes, and the double quotes of the value are encoded
    [InlineData("<p title='a\" onmouseover=\"alert(1)'>x</p>", "<p title=\"a&quot; onmouseover=&quot;alert(1)\">x</p>")]
    [InlineData("<p title=\"a' onmouseover='alert(1)\">x</p>", "<p title=\"a' onmouseover='alert(1)\">x</p>")]
    [InlineData("<p title=a\"onmouseover=alert(1)\"x>y</p>", "<p title=\"a&quot;onmouseover=alert(1)&quot;x\">y</p>")]
    [InlineData("<p title=a\"b'c>x</p>", "<p title=\"a&quot;b'c\">x</p>")]
    [InlineData("<p title=\"</p><script>alert(1)</script>\">x</p>", "<p title=\"&lt;/p&gt;&lt;script&gt;alert(1)&lt;/script&gt;\">x</p>")]
    // Entities in an attribute value are decoded by the parser and encoded again
    [InlineData("<p title='&lt;script&gt;'>x</p>", "<p title=\"&lt;script&gt;\">x</p>")]
    [InlineData("<p title=&quot;onmouseover=alert(1)&quot;>x</p>", "<p title=\"&quot;onmouseover=alert(1)&quot;\">x</p>")]
    [InlineData("<a href='http://example.com/?a=1&amp;b=2'>x</a>", "<a href=\"http://example.com/?a=1&amp;b=2\">x</a>")]
    // The URL is validated once decoded, as the browser reads it, so an entity cannot mask its scheme
    [InlineData("<a href='java&#115;cript:alert(1)'>x</a>", "<a href=\"\">x</a>")]
    [InlineData("<a href='javascript&#58;alert(1)'>x</a>", "<a href=\"\">x</a>")]
    [InlineData("<a href='java&#9;script:alert(1)'>x</a>", "<a href=\"\">x</a>")]
    [InlineData("<a href='&#x20;javascript:alert(1)'>x</a>", "<a href=\"\">x</a>")]
    public void Sanitize_AttributeValuesCannotEscapeTheirQuotes(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // Text is decoded by the parser and encoded again, so encoded markup stays encoded
    [InlineData("&lt;script&gt;alert(1)&lt;/script&gt;", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<p>&lt;img src=x onerror=alert(1)&gt;</p>", "<p>&lt;img src=x onerror=alert(1)&gt;</p>")]
    [InlineData("<p>&#x3c;script&#x3e;alert(1)&#x3c;/script&#x3e;</p>", "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>")]
    [InlineData("a &amp; b", "a &amp; b")]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("<p>a < b</p>", "<p>a &lt; b</p>")]
    // A browser reads a carriage return as a line feed, so a decoded one is written as a line feed
    [InlineData("a&#13;b&#13;&#10;c", "a\nb\n\nc")]
    [InlineData("<p title='a&#13;b'>x</p>", "<p title=\"a\nb\">x</p>")]
    // The parser ignores a line feed that directly follows <pre>, so a text that starts with one gets a second one
    [InlineData("<pre>\n\nx</pre>", "<pre>\n\nx</pre>")]
    [InlineData("<pre>\nx</pre>", "<pre>x</pre>")]
    [InlineData("<pre><object>\nx</object></pre>", "<pre>\n\nx</pre>")]
    public void Sanitize_EncodesText(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // The markup is read exactly as a browser reads it. An unterminated comment runs to the end of the fragment;
    // "<!x", "<?x" and "</0" are bogus comments that swallow everything up to the next ">". These comments are
    // removed, and a "<" that does not start any markup is text, so it is encoded.
    [InlineData("<b><!--", "<b></b>")]
    [InlineData("<p>a<!--b", "<p>a</p>")]
    [InlineData("<!--", "")]
    [InlineData("<b><!x", "<b></b>")]
    [InlineData("<b><?x", "<b></b>")]
    [InlineData("<b></0", "<b></b>")]
    [InlineData("<b><", "<b>&lt;</b>")]
    [InlineData("<p>a</0 onerror=alert(1)>b</p>", "<p>ab</p>")]
    [InlineData("<p>a<1 onerror=alert(1)>b</p>", "<p>a&lt;1 onerror=alert(1)&gt;b</p>")]
    [InlineData("<p>a<%img src=x onerror=alert(1)>b</p>", "<p>a&lt;%img src=x onerror=alert(1)&gt;b</p>")]
    [InlineData("<p>a<//script>b</p>", "<p>ab</p>")]
    // Already encoded text is not encoded a second time
    [InlineData("<p>&lt;script&gt;</p>", "<p>&lt;script&gt;</p>")]
    [InlineData("<p>a &amp; b</p>", "<p>a &amp; b</p>")]
    public void Sanitize_IncompleteMarkup(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Theory]
    // The parser allows a link in a link, or a heading in a heading, only when an element such as template or object
    // separates them. Unwrapping that element would make the output read back differently, so the inner one is
    // unwrapped too.
    [InlineData("<a href='https://a'>x<object><a href='https://b'>y</a></object></a>", "<a href=\"https://a\">xy</a>")]
    [InlineData("<a>x<template><b><a>y</a></b></template></a>", "<a>x<b>y</b></a>")]
    [InlineData("<h1>x<object><h2>y</h2></object></h1>", "<h1>xy</h1>")]
    [InlineData("<h1>x<object><b><h2>y</h2></b></object></h1>", "<h1>x<b><h2>y</h2></b></h1>")]
    // A table cell separates them too, so these are kept
    [InlineData("<a>x<table><tr><td><a>y</a></td></tr></table></a>", "<a>x<table><tbody><tr><td><a>y</a></td></tr></tbody></table></a>")]
    public void Sanitize_UnwrappingDoesNotNestLinksOrHeadings(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);
        Assert.Equal(actual, sanitizer.SanitizeHtmlFragment(actual));
    }

    [Fact]
    public void Sanitize_IsIdempotent()
    {
        // Sanitizing something already sanitized must be a no-op: an output that the parser reads back
        // differently from the way it was written is how mutation XSS gets started.
        string[] fragments =
        [
            "<b><!--", "<p>a<!--b", "<b><!x", "<b><?x", "<b></0", "<b></", "<b><",
            "-->", "--><img src=x onerror=alert(1)>", "<p>a</b <img src=x onerror=alert(1)>b</p>",
            "0\"?</<s->s\"\" 7v1v", "<td>]]></style></title><?x", "<table><!--", "<p><!x]]><!--<style>",
            "<![CDATA[a><script>alert(1)</script>]]>", "<p>a < b</p>", "<p>&lt;script&gt;</p>",
            "<svg><table></0<!--", "<a href='javascript:alert(1)'></td><!--<script>>",
            "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
            "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
            "<form><p>a</p>b<span>c</span></form>", "<p title=a\"b'c>x</p>",
            "a&#13;b", "<p title='a&#13;b'>x</p>", "x&#13;<script></script>&#10;y",
            "<pre>\n\nx</pre>", "<pre><object>\nx</object></pre>", "<pre>&#10;&#10;x</pre>",
        ];

        foreach (var fragment in fragments)
        {
            var once = new HtmlSanitizer().SanitizeHtmlFragment(fragment);
            var twice = new HtmlSanitizer().SanitizeHtmlFragment(once);
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void Sanitize_IsIdempotent_Fuzz()
    {
        // Unwrapping an element can leave another one where the parser never puts it ("<li><select><li>" would
        // become an li nested in an li, "<template><tbody>" a tbody outside of a table), so such an element is
        // unwrapped too, and the parser reads the output back with the same structure.
        string[] tokens =
        [
            "<b>", "</b>", "<script>", "</script>", "<img src=x onerror=alert(1)>",
            "<!--", "-->", "<![CDATA[", "]]>", "<svg>", "</svg>", "<style>", "</style>", "<form>", "</form>",
            "<a href='javascript:alert(1)'>", "</a>", "a", "<", ">", "\"", "'", "=", "/", "</0", "<?x", "<!x",
            "<title>", "</title>", "<textarea>", "</textarea>", "<noscript>", "</noscript>", "<plaintext>",
            "<math>", "<div>", "</div>", "<span title=\"", "\">", "<iframe>", "</iframe>",
            "<p>", "</p>", "<li>", "<dd>", "<dt>", "<ul>", "<table>", "</table>", "<tbody>", "<tr>", "<td>", "<caption>", "<col>",
            "<template>", "</template>", "<object>", "</object>", "<select>", "<rt>", "<rp>", "<ruby>", "<h1>", "<hr>",
        ];

        var random = new Random(Seed: 20260827);
        for (var i = 0; i < 50_000; i++)
        {
            var builder = new StringBuilder();
            for (var j = random.Next(1, 6); j > 0; j--)
            {
                builder.Append(tokens[random.Next(tokens.Length)]);
            }

            var fragment = builder.ToString();
            var once = new HtmlSanitizer().SanitizeHtmlFragment(fragment);
            var twice = new HtmlSanitizer().SanitizeHtmlFragment(once);
            Assert.Equal(once, twice, message: $"Not idempotent for {fragment}");
        }
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
    [InlineData("<p><!--a--!>b--></p>", "<p><!--a-->b--&gt;</p>")]
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
    // The noscript element is removed with its content, whether a parser reads it as markup (scripting disabled)
    // or as raw text (scripting enabled), so the difference cannot be used to smuggle markup
    [InlineData("<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">", "")]
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
    public void Sanitize_AllowedTemplateWithIsValue()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("template");

        Assert.Equal("<template><p>x</p></template>", sanitizer.SanitizeHtmlFragment("<template is='x-foo'><p is='x-bar'>x</p></template>"));
    }

    [Fact]
    public void Sanitize_CanAllowIsAttribute()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Add("is");

        Assert.Equal("<p is=\"x-foo\">x</p>", sanitizer.SanitizeHtmlFragment("<p is='x-foo'>x</p>"));
    }

    [Fact]
    public void Sanitize_AllowedTemplateContentIsSanitized()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("template");

        Assert.Equal("<template><p>x</p></template>", sanitizer.SanitizeHtmlFragment("<template><p onclick='alert(1)'>x</p><script>alert(1)</script></template>"));
    }

    [Theory]
    // SVG and MathML elements are parsed with other rules than HTML elements, so they are never kept, and the
    // elements they contain are not HTML elements even when they have the same name (the "a" of SVG)
    [InlineData("<svg><a href='https://example.com'>x</a></svg>", "x")]
    [InlineData("<math><mi>x</mi></math>", "x")]
    public void Sanitize_ForeignElementsAreUnwrappedEvenWhenAllowed(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("svg");
        sanitizer.ValidElements.Add("math");
        sanitizer.ValidElements.Add("mi");

        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Fact]
    public void Sanitize_CanAllowAdditionalAttributes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Add("data-custom");

        Assert.Equal("<p data-custom=\"1\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-custom='1'>test</p>"));
    }

    [Fact]
    public void Sanitize_CanDisallowAttributes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Remove("target");

        Assert.Equal("<a href=\"https://example.com\">test</a>", sanitizer.SanitizeHtmlFragment("<a href='https://example.com' target='_blank'>test</a>"));
    }

    [Fact]
    public void Sanitize_CanAllowAdditionalAttributePrefixes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributePrefixes.Add("data-");

        Assert.Equal("<p data-a=\"1\" data-b=\"2\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-a='1' data-b='2' style='a'>test</p>"));
    }

    [Fact]
    public void Sanitize_CanDisallowAttributePrefixes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributePrefixes.Remove("aria-");

        Assert.Equal("<p role=\"note\">test</p>", sanitizer.SanitizeHtmlFragment("<p role='note' aria-label='a'>test</p>"));
    }

    [Fact]
    public void Sanitize_AttributesAllowedByPrefixAreUrlValidated()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributePrefixes.Add("data-");
        sanitizer.UriAttributes.Add("data-url");

        Assert.Equal("<p data-url=\"\" data-x=\"javascript:alert(1)\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-url='javascript:alert(1)' data-x='javascript:alert(1)'>test</p>"));
    }

    [Fact]
    public void Sanitize_CanAddUriAttributes()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Add("data-url");
        sanitizer.UriAttributes.Add("data-url");

        Assert.Equal("<p data-url=\"\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-url='javascript:alert(1)'>test</p>"));
    }

    [Theory]
    // The parser ignores a form start tag in a form, so a nested form is unwrapped, unless a template separates them
    [InlineData("<form><template><form>x</form></template></form>", "<form>x</form>")]
    [InlineData("<form><div></form><form>x</form>", "<form><div>x</div></form>")]
    [InlineData("<form><template><b><form>x</form></b></template></form>", "<form><b>x</b></form>")]
    [InlineData("<form><template><form>x</form></template></form><form>y</form>", "<form>x</form><form>y</form>")]
    public void Sanitize_AllowedFormsAreNotNested(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("form");

        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);
        Assert.Equal(actual, sanitizer.SanitizeHtmlFragment(actual));
    }

    [Theory]
    // A start tag closes an open element (a button in scope, a p in button scope, the open li, ...), or the parser
    // ignores it, so an element that unwrapping puts in such an element is unwrapped too
    [InlineData("<button>a<template><b><button>b</button></b></template></button>", "<button>a<b>b</b></button>")]
    [InlineData("<button>a<object><button>b</button></object></button>", "<button>a<object><button>b</button></object></button>")]
    [InlineData("<nobr>a<template><nobr>b</nobr></template></nobr>", "<nobr>ab</nobr>")]
    [InlineData("<p>a<template><div>b</div></template></p>", "<p>ab</p>")]
    [InlineData("<p>a<template><ul><li>b</li></ul><hr><table><tr><td>c</td></tr></table></template></p>", "<p>abc</p>")]
    [InlineData("<p>a<button><div>b</div></button></p>", "<p>a<button><div>b</div></button></p>")]
    [InlineData("<li>a<template><b><li>b</li></b></template></li>", "<li>a<b>b</b></li>")]
    [InlineData("<li>a<ul><li>b</li></ul></li>", "<li>a<ul><li>b</li></ul></li>")]
    [InlineData("<dl><dd>a<template><dt>b</dt></template></dd></dl>", "<dl><dd>ab</dd></dl>")]
    [InlineData("<select><template><select>a</select><input></template></select>", "<select>a</select>")]
    [InlineData("<select><option>a<template><option>b</option></template></option></select>", "<select><option>ab</option></select>")]
    [InlineData("<option>a<template><option>b</option></template></option>", "<option>ab</option>")]
    [InlineData("<ruby>a<p><template><rt>b</rt></template></p></ruby>", "<ruby>a<p>b</p></ruby>")]
    [InlineData("<div><template><tr><td>a</td></tr></template></div>", "<div>a</div>")]
    [InlineData("<table><template><tr><td>a</td></tr></template></table>", "a")]
    [InlineData("<table><tbody><tr><td>a</td><template><b>b</b></template></tr></tbody></table>", "a<b>b</b>")]
    [InlineData("<table><tbody><tr><td>a</td><template> </template></tr></tbody></table>", "<table><tbody><tr><td>a</td> </tr></tbody></table>")]
    public void Sanitize_UnwrappingDoesNotMisnestElements(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.UnionWith(["button", "nobr", "select", "option", "input", "object"]);

        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);
        Assert.Equal(actual, sanitizer.SanitizeHtmlFragment(actual));
    }

    [Theory]
    // The parser inserts a tbody between a table and a tr, and moves the text of a table before it
    [InlineData("<table><tbody><tr><td>a</td></tr></tbody></table>", "tbody", "a")]
    [InlineData("<table><tbody><tr><td>a</td></tr></tbody></table>", "td", "a")]
    [InlineData("<table><tbody><tr><td> </td></tr></tbody></table>", "td", "<table><tbody><tr> </tr></tbody></table>")]
    [InlineData("<table><colgroup><col></colgroup></table>", "colgroup", "<table></table>")]
    public void Sanitize_DisallowedTablePartUnwrapsTheTable(string html, string disallowedElement, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Remove(disallowedElement);

        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);
        Assert.Equal(actual, sanitizer.SanitizeHtmlFragment(actual));
    }

    [Theory]
    // The first start tag of the content of a template decides how the parser reads the next ones
    [InlineData("<template><caption><button>a</button></caption><tbody><tr><td>b</td></tr></tbody></template>", "<template><button>a</button>b</template>")]
    [InlineData("<template><tr><td>a</td></tr><tr><td>b</td></tr></template>", "<template><tr><td>a</td></tr><tr><td>b</td></tr></template>")]
    [InlineData("<template><td>a</td><td>b</td></template>", "<template><td>a</td><td>b</td></template>")]
    public void Sanitize_AllowedTemplateWithTableParts(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.UnionWith(["template", "button"]);
        sanitizer.ValidElements.Remove("caption");

        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);
        Assert.Equal(actual, sanitizer.SanitizeHtmlFragment(actual));
    }

    [Fact]
    public void Sanitize_TableInParagraphIsReadAsInAPageInStandardsMode()
    {
        // The parser closes the p element (a document in quirks mode would keep the table in it), and the end tag of
        // the p element creates an empty one
        var sanitizer = new HtmlSanitizer();
        Assert.Equal("<p>a</p><table><tbody><tr><td>b</td></tr></tbody></table><p></p>", sanitizer.SanitizeHtmlFragment("<p>a<table><tr><td>b</td></tr></table></p>"));
    }

    [Fact]
    public void Sanitize_AllowedTemplateSeparatesForms()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("form");
        sanitizer.ValidElements.Add("template");

        var actual = sanitizer.SanitizeHtmlFragment("<form><template><form>x</form></template></form>");
        Assert.Equal("<form><template><form>x</form></template></form>", actual);
        Assert.Equal(actual, sanitizer.SanitizeHtmlFragment(actual));
    }

    [Theory]
    // The value is a safe URL (its scheme is https) but an unsafe srcset value, and the reverse
    [InlineData("<img srcset='https://example.com/a.png 1x, javascript:alert(1) 2x'>", "<img srcset=\"\">")]
    [InlineData("<a href='https://example.com/a.png 1x, javascript:alert(1) 2x'>x</a>", "<a href=\"\">x</a>")]
    [InlineData("<a href='java&#115;cript:alert(1)'>x</a>", "<a href=\"\">x</a>")]
    [InlineData("<img srcset='https://example.com/a.png 1x, https://example.com/b.png 2x'>", "<img srcset=\"https://example.com/a.png 1x, https://example.com/b.png 2x\">")]
    public void Sanitize_AttributeInUriAndSrcsetAttributesPassesBothValidations(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.UriAttributes.Add("srcset");
        sanitizer.SrcsetAttributes.Add("href");

        Assert.Equal(expectedResult, sanitizer.SanitizeHtmlFragment(html));
    }

    [Fact]
    public void DocumentationExample_SanitizeComment()
    {
        #region HtmlSanitizer_SanitizeComment
        var sanitizer = new HtmlSanitizer();

        var comment = """<p onclick="steal()">Nice <b>post</b>! <a href="javascript:alert(1)">Click</a> <img src="cat.png" onerror="alert(1)"><script>alert(2)</script></p>""";
        var safeHtml = sanitizer.SanitizeHtmlFragment(comment);

        Assert.Equal("""<p>Nice <b>post</b>! <a href="">Click</a> <img src="cat.png"></p>""", safeHtml);
        #endregion
    }

    [Fact]
    public void DocumentationExample_DefaultAttributes()
    {
        #region HtmlSanitizer_DefaultAttributes
        var sanitizer = new HtmlSanitizer();

        var result = sanitizer.SanitizeHtmlFragment("<p id='intro' class='note' title='Tip' aria-label='Tip' onclick='alert(1)' style='color:red'>x</p>");
        Assert.Equal("""<p class="note" title="Tip" aria-label="Tip">x</p>""", result);
        #endregion
    }

    [Fact]
    public void DocumentationExample_ForeignContent()
    {
        #region HtmlSanitizer_ForeignContent
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Add("svg");

        // SVG and MathML elements are always unwrapped: only their text is kept
        var result = sanitizer.SanitizeHtmlFragment("<p><svg onload='alert(1)'><text>Hello</text></svg></p>");
        Assert.Equal("<p>Hello</p>", result);
        #endregion
    }

    [Fact]
    public void DocumentationExample_AllowElements()
    {
        #region HtmlSanitizer_AllowElements
        var sanitizer = new HtmlSanitizer();
        var html = "<details open><summary>More</summary><p>Text</p></details>";

        // details and summary are not allowed by default, so they are unwrapped
        Assert.Equal("More<p>Text</p>", sanitizer.SanitizeHtmlFragment(html));

        sanitizer.ValidElements.Add("details");
        sanitizer.ValidElements.Add("summary");
        Assert.Equal("""<details open=""><summary>More</summary><p>Text</p></details>""", sanitizer.SanitizeHtmlFragment(html));
        #endregion
    }

    [Fact]
    public void DocumentationExample_RestrictElements()
    {
        #region HtmlSanitizer_RestrictElements
        // Only keep inline formatting and links
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidElements.Clear();
        sanitizer.ValidElements.UnionWith(["a", "b", "br", "code", "em", "i", "strong"]);

        var result = sanitizer.SanitizeHtmlFragment("<h1>Title</h1><p>Some <b>bold</b> text<img src='https://example.com/a.png'></p>");
        Assert.Equal("TitleSome <b>bold</b> text", result);
        #endregion
    }

    [Fact]
    public void DocumentationExample_StyleIsNotSanitized()
    {
        #region HtmlSanitizer_StyleIsNotSanitized
        var sanitizer = new HtmlSanitizer();
        sanitizer.ValidAttributes.Add("style");

        // The CSS is kept as is, including the URL that it loads
        var result = sanitizer.SanitizeHtmlFragment("<p style='background:url(https://tracker.example/pixel)'>x</p>");
        Assert.Equal("""<p style="background:url(https://tracker.example/pixel)">x</p>""", result);
        #endregion
    }

    [Fact]
    public void DocumentationExample_UrlKinds()
    {
        #region HtmlSanitizer_UrlKinds
        var sanitizer = new HtmlSanitizer();

        // Relative URLs and the allowed schemes are kept
        Assert.Equal("""<a href="/account/settings">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='/account/settings'>x</a>"));
        Assert.Equal("""<a href="mailto:user@example.com">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='mailto:user@example.com'>x</a>"));

        // data: URLs are kept only for base64-encoded images, videos and audio
        Assert.Equal("""<img src="data:image/png;base64,iVBORw0KGgo=">""", sanitizer.SanitizeHtmlFragment("<img src='data:image/png;base64,iVBORw0KGgo='>"));
        Assert.Equal("""<img src="">""", sanitizer.SanitizeHtmlFragment("<img src='data:image/svg+xml;base64,PHN2Zz4='>"));

        // Any other scheme empties the attribute, and the element is kept
        Assert.Equal("""<a href="">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='vbscript:msgbox(1)'>x</a>"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_EncodedUrls()
    {
        #region HtmlSanitizer_EncodedUrls
        var sanitizer = new HtmlSanitizer();

        // The value is validated once decoded, as the browser reads it
        Assert.Equal("""<a href="">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='java&#115;cript:alert(1)'>x</a>"));
        Assert.Equal("""<a href="https://example.com/?a=1&amp;b=2">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='https://example.com/?a=1&amp;b=2'>x</a>"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Idempotent()
    {
        #region HtmlSanitizer_Idempotent
        var sanitizer = new HtmlSanitizer();

        // object is not allowed: unwrapping it would put a link in a link, so the inner link is unwrapped too
        var once = sanitizer.SanitizeHtmlFragment("<a href='https://a.example'>x<object><a href='https://b.example'>y</a></object></a>");
        Assert.Equal("""<a href="https://a.example">xy</a>""", once);

        // The output is read back as the same elements, so sanitizing it again changes nothing
        Assert.Equal(once, sanitizer.SanitizeHtmlFragment(once));
        #endregion
    }

    [Fact]
    public void DocumentationExample_TemplateExpressions()
    {
        #region HtmlSanitizer_TemplateExpressions
        var sanitizer = new HtmlSanitizer();

        // Text is kept as text: a client-side framework that compiles this markup as a template would evaluate it
        var result = sanitizer.SanitizeHtmlFragment("<p>{{constructor.constructor('alert(1)')()}}</p>");
        Assert.Equal("<p>{{constructor.constructor('alert(1)')()}}</p>", result);
        #endregion
    }
}
