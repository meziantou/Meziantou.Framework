using NUnit.Framework;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines
{
    [TestFixture]
    public class TestImageInline
    {
        [TestCase("![](a)")]
        [TestCase(" ![](a)")]
        [TestCase("![](a) ")]
        [TestCase(" ![](a) ")]
        [TestCase("   ![description](http://example.com)")]
        public void Test(string value)
        {
            RoundTrip(value);
        }

        [TestCase("paragraph   ![description](http://example.com)")]
        public void TestParagraph(string value)
        {
            RoundTrip(value);
        }
    }
}
