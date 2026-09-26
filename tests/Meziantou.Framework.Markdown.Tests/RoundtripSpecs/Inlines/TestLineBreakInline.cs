using NUnit.Framework;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines
{
    [TestFixture]
    public class TestLineBreakInline
    {
        [TestCase("p\n")]
        [TestCase("p\r\n")]
        [TestCase("p\r")]
        [TestCase("[]() ![]()  ``  ` `  `  `  ![]()   ![]()")]
        public void Test(string value)
        {
            RoundTrip(value);
        }
    }
}
