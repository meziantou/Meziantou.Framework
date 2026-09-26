using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

[TestFixture]
public class TestNoBlocksFoundBlock
{
    [TestCase("\r")]
    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\t")]
    [TestCase("\v")]
    [TestCase("\f")]
    [TestCase(" ")]
    [TestCase("  ")]
    [TestCase("   ")]
    public void Test(string value)
    {
        RoundTrip(value);
    }
}
