using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestParagraph
{
    [Theory]
    [InlineData("p")]
    [InlineData(" p")]
    [InlineData("p ")]
    [InlineData(" p ")]

    [InlineData("p\np")]
    [InlineData(" p\np")]
    [InlineData("p \np")]
    [InlineData(" p \np")]

    [InlineData("p\n p")]
    [InlineData(" p\n p")]
    [InlineData("p \n p")]
    [InlineData(" p \n p")]

    [InlineData("p\np ")]
    [InlineData(" p\np ")]
    [InlineData("p \np ")]
    [InlineData(" p \np ")]

    [InlineData("p\n\n p ")]
    [InlineData(" p\n\n p ")]
    [InlineData("p \n\n p ")]
    [InlineData(" p \n\n p ")]

    [InlineData("p\n\np")]
    [InlineData(" p\n\np")]
    [InlineData("p \n\np")]
    [InlineData(" p \n\np")]

    [InlineData("p\n\n p")]
    [InlineData(" p\n\n p")]
    [InlineData("p \n\n p")]
    [InlineData(" p \n\n p")]

    [InlineData("p\n\np ")]
    [InlineData(" p\n\np ")]
    [InlineData("p \n\np ")]
    [InlineData(" p \n\np ")]


    [InlineData("\np")]
    [InlineData("\n p")]
    [InlineData("\np ")]
    [InlineData("\n p ")]

    [InlineData("\np\np")]
    [InlineData("\n p\np")]
    [InlineData("\np \np")]
    [InlineData("\n p \np")]

    [InlineData("\np\n p")]
    [InlineData("\n p\n p")]
    [InlineData("\np \n p")]
    [InlineData("\n p \n p")]

    [InlineData("\np\np ")]
    [InlineData("\n p\np ")]
    [InlineData("\np \np ")]
    [InlineData("\n p \np ")]

    [InlineData("\np\n\n p ")]
    [InlineData("\n p\n\n p ")]
    [InlineData("\np \n\n p ")]
    [InlineData("\n p \n\n p ")]

    [InlineData("\np\n\np")]
    [InlineData("\n p\n\np")]
    [InlineData("\np \n\np")]
    [InlineData("\n p \n\np")]

    [InlineData("\np\n\n p")]
    [InlineData("\n p\n\n p")]
    [InlineData("\np \n\n p")]
    [InlineData("\n p \n\n p")]

    [InlineData("\np\n\np ")]
    [InlineData("\n p\n\np ")]
    [InlineData("\np \n\np ")]
    [InlineData("\n p \n\np ")]


    [InlineData("p  p")]
    [InlineData("p\tp")]
    [InlineData("p \tp")]
    [InlineData("p \t p")]

    // special cases
    [InlineData(" p \n\n\n\n p \n\n")]
    [InlineData("\n\np")]
    [InlineData("p\n")]
    [InlineData("p\n\n")]
    [InlineData("p\np\n p")]
    [InlineData("p\np\n p\n p")]
    public void Test(string value)
    {
        RoundTrip(value);
    }


    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]

    [InlineData("p\n")]
    [InlineData("p\r")]
    [InlineData("p\r\n")]

    [InlineData("p\np")]
    [InlineData("p\rp")]
    [InlineData("p\r\np")]

    [InlineData("p\np\n")]
    [InlineData("p\rp\n")]
    [InlineData("p\r\np\n")]

    [InlineData("p\np\r")]
    [InlineData("p\rp\r")]
    [InlineData("p\r\np\r")]

    [InlineData("p\np\r\n")]
    [InlineData("p\rp\r\n")]
    [InlineData("p\r\np\r\n")]

    [InlineData("\np\n")]
    [InlineData("\np\r")]
    [InlineData("\np\r\n")]

    [InlineData("\np\np")]
    [InlineData("\np\rp")]
    [InlineData("\np\r\np")]

    [InlineData("\np\np\n")]
    [InlineData("\np\rp\n")]
    [InlineData("\np\r\np\n")]

    [InlineData("\np\np\r")]
    [InlineData("\np\rp\r")]
    [InlineData("\np\r\np\r")]

    [InlineData("\np\np\r\n")]
    [InlineData("\np\rp\r\n")]
    [InlineData("\np\r\np\r\n")]

    [InlineData("\rp\n")]
    [InlineData("\rp\r")]
    [InlineData("\rp\r\n")]

    [InlineData("\rp\np")]
    [InlineData("\rp\rp")]
    [InlineData("\rp\r\np")]

    [InlineData("\rp\np\n")]
    [InlineData("\rp\rp\n")]
    [InlineData("\rp\r\np\n")]

    [InlineData("\rp\np\r")]
    [InlineData("\rp\rp\r")]
    [InlineData("\rp\r\np\r")]

    [InlineData("\rp\np\r\n")]
    [InlineData("\rp\rp\r\n")]
    [InlineData("\rp\r\np\r\n")]

    [InlineData("\r\np\n")]
    [InlineData("\r\np\r")]
    [InlineData("\r\np\r\n")]

    [InlineData("\r\np\np")]
    [InlineData("\r\np\rp")]
    [InlineData("\r\np\r\np")]

    [InlineData("\r\np\np\n")]
    [InlineData("\r\np\rp\n")]
    [InlineData("\r\np\r\np\n")]

    [InlineData("\r\np\np\r")]
    [InlineData("\r\np\rp\r")]
    [InlineData("\r\np\r\np\r")]

    [InlineData("\r\np\np\r\n")]
    [InlineData("\r\np\rp\r\n")]
    [InlineData("\r\np\r\np\r\n")]

    [InlineData("p\n\n")]
    [InlineData("p\n\n\n")]
    [InlineData("p\n\n\n\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(" \n")]
    [InlineData(" \r")]
    [InlineData(" \r\n")]

    [InlineData(" \np")]
    [InlineData(" \rp")]
    [InlineData(" \r\np")]

    [InlineData("  \np")]
    [InlineData("  \rp")]
    [InlineData("  \r\np")]

    [InlineData("   \np")]
    [InlineData("   \rp")]
    [InlineData("   \r\np")]

    [InlineData(" \n ")]
    [InlineData(" \r ")]
    [InlineData(" \r\n ")]

    [InlineData(" \np ")]
    [InlineData(" \rp ")]
    [InlineData(" \r\np ")]

    [InlineData("  \np ")]
    [InlineData("  \rp ")]
    [InlineData("  \r\np ")]

    [InlineData("   \np ")]
    [InlineData("   \rp ")]
    [InlineData("   \r\np ")]
    public void Test_WhitespaceWithNewline(string value)
    {
        RoundTrip(value);
    }
}
