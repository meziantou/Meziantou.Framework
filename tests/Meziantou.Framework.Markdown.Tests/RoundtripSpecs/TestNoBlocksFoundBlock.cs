using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestNoBlocksFoundBlock
{
    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData("\v")]
    [InlineData("\f")]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("   ")]
    public void Test(string value)
    {
        RoundTrip(value);
    }
}
