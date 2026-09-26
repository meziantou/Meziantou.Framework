using NUnit.Framework;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines
{
    [TestFixture]
    public class TestBackslashEscapeInline
    {
        [TestCase(@"\!")]
        [TestCase(@"\""")]
        [TestCase(@"\#")]
        [TestCase(@"\$")]
        [TestCase(@"\&")]
        [TestCase(@"\'")]
        [TestCase(@"\(")]
        [TestCase(@"\)")]
        [TestCase(@"\*")]
        [TestCase(@"\+")]
        [TestCase(@"\,")]
        [TestCase(@"\-")]
        [TestCase(@"\.")]
        [TestCase(@"\/")]
        [TestCase(@"\:")]
        [TestCase(@"\;")]
        [TestCase(@"\<")]
        [TestCase(@"\=")]
        [TestCase(@"\>")]
        [TestCase(@"\?")]
        [TestCase(@"\@")]
        [TestCase(@"\[")]
        [TestCase(@"\\")]
        [TestCase(@"\]")]
        [TestCase(@"\^")]
        [TestCase(@"\_")]
        [TestCase(@"\`")]
        [TestCase(@"\{")]
        [TestCase(@"\|")]
        [TestCase(@"\}")]
        [TestCase(@"\~")]

        // below test breaks visual studio
        //[TestCase(@"\!\""\#\$\%\&\'\(\)\*\+\,\-\.\/\:\;\<\=\>\?\@\[\\\]\^\_\`\{\|\}\~")]
        public void Test(string value)
        {
            RoundTrip(value);
        }

        [TestCase(@"# \#\#h1")]
        [TestCase(@"# \#\#h1\#")]
        public void TestHeading(string value)
        {
            RoundTrip(value);
        }

        [TestCase(@"`\``")]
        [TestCase(@"` \``")]
        [TestCase(@"`\` `")]
        [TestCase(@"` \` `")]
        [TestCase(@" ` \` `")]
        [TestCase(@"` \` ` ")]
        [TestCase(@" ` \` ` ")]
        public void TestCodeSpanInline(string value)
        {
            RoundTrip(value);
        }
    }
}
