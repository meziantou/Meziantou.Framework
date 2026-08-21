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
    // Comments are removed: they are serialized verbatim, and browsers end them at "--!>" or treat
    // "<!-->" as a complete comment, which would turn the content into live markup
    [InlineData("<!-- comment -->test", "test")]
    [InlineData("<p>a<!--<script>-->b</p>", "<p>ab</p>")]
    [InlineData("<!--x--!><img src=x onerror=alert(1)>-->", "")]
    [InlineData("<!--[if IE]><img src=x onerror=alert(1)><![endif]-->", "")]
    // CDATA sections are removed: browsers parse "<![CDATA[" as a comment ending at the first ">"
    [InlineData("<![CDATA[> <img src=x onerror=alert(1)>]]>", "")]
    [InlineData("<p><![CDATA[<script>alert(1)</script>]]></p>", "<p></p>")]
    // Markup left in text nodes by the parser's error recovery is escaped
    [InlineData("<!--> <img src=x onerror=alert(1)>", "&lt;&lt;!--&gt; &lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("<div>a</div", "<div>a&lt;/div</div>")]
    [InlineData("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>")]
    [InlineData("<p>a &amp; b</p>", "<p>a &amp; b</p>")]
    // Prefixed attributes are subject to the allow list like any other attribute
    [InlineData("<p foo:onclick='alert(1)'>test</p>", "<p>test</p>")]
    [InlineData("<p v-on:click='alert(1)'>test</p>", "<p>test</p>")]
    [InlineData("<p xmlns:x='y' x:onclick='alert(1)'>test</p>", "<p>test</p>")]
    [InlineData("<a xlink:href='javascript:alert(1)'>test</a>", "<a xlink:href=''>test</a>")]
    [InlineData("<a xlink:href='https://example.com'>test</a>", "<a xlink:href='https://example.com'>test</a>")]
    public void Sanitize(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        var actual = sanitizer.SanitizeHtmlFragment(html);
        Assert.Equal(expectedResult, actual);

        // Sanitizing an already-sanitized fragment must be a no-op. If it is not, the output does not
        // re-parse to the tree it was serialized from, which is how markup smuggles itself back in.
        Assert.Equal(actual, sanitizer.SanitizeHtmlFragment(actual));
    }
}
