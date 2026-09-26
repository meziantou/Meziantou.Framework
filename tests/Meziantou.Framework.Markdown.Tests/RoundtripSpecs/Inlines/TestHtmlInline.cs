using NUnit.Framework;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines
{
    [TestFixture]
    public class TestHtmlInline
    {
        [TestCase("<em>f</em>")]
        [TestCase("<em> f</em>")]
        [TestCase("<em>f </em>")]
        [TestCase("<em> f </em>")]
        [TestCase("<b>p</b>")]
        [TestCase("<b></b>")]
        [TestCase("<b> </b>")]
        [TestCase("<b>  </b>")]
        [TestCase("<b>   </b>")]
        [TestCase("<b>\t</b>")]
        [TestCase("<b> \t</b>")]
        [TestCase("<b>\t </b>")]
        [TestCase("<b> \t </b>")]
        public void Test(string value)
        {
            RoundTrip(value);
        }
    }
}
