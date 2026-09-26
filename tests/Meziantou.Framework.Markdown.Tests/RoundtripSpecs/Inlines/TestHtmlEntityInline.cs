using NUnit.Framework;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines
{
    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="https://spec.commonmark.org/0.29/#entity-and-numeric-character-references"/>
    [TestFixture]
    public class TestHtmlEntityInline
    {
        [TestCase("&gt;")]
        [TestCase("&lt;")]
        [TestCase("&nbsp;")]
        [TestCase("&heartsuit;")]
        [TestCase("&#42;")]
        [TestCase("&#0;")]
        [TestCase("&#1234;")]
        [TestCase("&#xcab;")]

        [TestCase(" &gt;")]
        [TestCase(" &lt;")]
        [TestCase(" &nbsp;")]
        [TestCase(" &heartsuit;")]
        [TestCase(" &#42;")]
        [TestCase(" &#0;")]
        [TestCase(" &#1234;")]
        [TestCase(" &#xcab;")]

        [TestCase("&gt; ")]
        [TestCase("&lt; ")]
        [TestCase("&nbsp; ")]
        [TestCase("&heartsuit; ")]
        [TestCase("&#42; ")]
        [TestCase("&#0; ")]
        [TestCase("&#1234; ")]
        [TestCase("&#xcab; ")]

        [TestCase(" &gt; ")]
        [TestCase(" &lt; ")]
        [TestCase(" &nbsp; ")]
        [TestCase(" &heartsuit; ")]
        [TestCase(" &#42; ")]
        [TestCase(" &#0; ")]
        [TestCase(" &#1234; ")]
        [TestCase(" &#xcab; ")]
        public void Test(string value)
        {
            RoundTrip(value);
        }
    }
}
