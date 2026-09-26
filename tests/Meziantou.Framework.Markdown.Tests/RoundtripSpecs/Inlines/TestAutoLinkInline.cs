using System.IO;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using NUnit.Framework;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines
{
    [TestFixture]
    public class TestAutoLinkInline
    {
        [TestCase("<http://a>")]
        [TestCase(" <http://a>")]
        [TestCase("<http://a> ")]
        [TestCase(" <http://a> ")]
        [TestCase("<example@example.com>")]
        [TestCase(" <example@example.com>")]
        [TestCase("<example@example.com> ")]
        [TestCase(" <example@example.com> ")]
        [TestCase("p http://a p")]
        public void Test(string value)
        {
            RoundTrip(value);
        }

        [TestCase("http://example.com/", "[http://example.com/](http://example.com/)")]
        [TestCase("www.example.com", "[www.example.com](http://www.example.com)")]
        [TestCase("mailto:user@example.com", "[user@example.com](mailto:user@example.com)")]
        public void AutoLinksKeepUrlWhenRoundTripped(string markdown, string expected)
        {
            var pipeline = new MarkdownPipelineBuilder()
                .DisableHtml()
                .UseAutoLinks()
                .EnableTrackTrivia()
                .Build();
            MarkdownDocument markdownDocument = MarkdownConverter.Parse(markdown, pipeline);
            var sw = new StringWriter();
            var rr = new RoundtripRenderer(sw);

            rr.Write(markdownDocument);

            Assert.AreEqual(expected, sw.ToString());
        }
    }
}
