using Xunit;

namespace Meziantou.Framework.Sanitizers.Tests
{
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
        [InlineData("<a href='javascript:alert('toto')'>test</a>", "<a href=\"\">test</a>")]
        [InlineData("<a href='https://example.com'>test</a>", "<a href=\"https://example.com\">test</a>")]
        public void Sanitize(string html, string expectedResult)
        {
            var sanitizer = new HtmlSanitizer();
            var actual = sanitizer.SanitizeHtmlFragment(html);
            Assert.Equal(expectedResult, actual);
        }
    }
}
