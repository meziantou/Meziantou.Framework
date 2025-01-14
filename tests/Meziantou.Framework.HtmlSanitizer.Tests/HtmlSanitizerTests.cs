using System.Diagnostics;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using FluentAssertions;
using Xunit;

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
    [InlineData("<a href='javascript:alert('toto')'>test</a>", "<a href=''>test</a>")]
    [InlineData("<a href='https://example.com'>test</a>", "<a href='https://example.com'>test</a>")]
    [InlineData("<img srcset='javascript:alert() 300w, https://example.com 600w'>", "<img srcset=''>")]
    [InlineData("<img srcset='https://example.com 300w, https://example.com 600w'>", "<img srcset='https://example.com 300w, https://example.com 600w'>")]
    public void Sanitize(string html, string expectedResult)
    {
        var sanitizer = new HtmlSanitizer();
        var actual = sanitizer.SanitizeHtmlFragment(html);
        AreEquivalent(expectedResult, actual);
    }

    [DebuggerStepThrough]
    private static void AreEquivalent(string expected, string actual)
    {
        AreEquivalent(expected, actual, ignoreSpaces: false);
    }

    [DebuggerStepThrough]
    private static void AreEquivalent(string expected, string actual, bool ignoreSpaces)
    {
        var parser = new HtmlParser();
        var expectedDocument = parser.ParseDocument(expected);
        var actualDocument = parser.ParseDocument(actual);

        if (ignoreSpaces)
        {
            Assert.Equal(FormatDocument(expectedDocument), FormatDocument(actualDocument));
        }
        else
        {
            Assert.Equal(expectedDocument.Body.InnerHtml, actualDocument.Body.InnerHtml);
        }
    }

    private static string FormatDocument(IHtmlDocument document)
    {
        using var sw = new StringWriter();
        document.Body.ToHtml(sw, new PrettyMarkupFormatter());
        return sw.ToString();
    }
}
