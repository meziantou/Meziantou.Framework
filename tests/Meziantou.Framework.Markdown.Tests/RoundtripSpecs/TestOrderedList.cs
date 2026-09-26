using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestOrderedList
{
    [Theory]
    [InlineData("1. i")]
    [InlineData("1.  i")]
    [InlineData("1. i ")]
    [InlineData("1.  i ")]
    [InlineData("1.  i  ")]

    [InlineData(" 1. i")]
    [InlineData(" 1.  i")]
    [InlineData(" 1. i ")]
    [InlineData(" 1.  i ")]
    [InlineData(" 1.  i  ")]

    [InlineData("  1. i")]
    [InlineData("  1.  i")]
    [InlineData("  1. i ")]
    [InlineData("  1.  i ")]
    [InlineData("  1.  i  ")]

    [InlineData("   1. i")]
    [InlineData("   1.  i")]
    [InlineData("   1. i ")]
    [InlineData("   1.  i ")]
    [InlineData("   1.  i  ")]

    [InlineData("1. i\n")]
    [InlineData("1.  i\n")]
    [InlineData("1. i \n")]
    [InlineData("1.  i \n")]
    [InlineData("1.  i  \n")]

    [InlineData(" 1. i\n")]
    [InlineData(" 1.  i\n")]
    [InlineData(" 1. i \n")]
    [InlineData(" 1.  i \n")]
    [InlineData(" 1.  i  \n")]

    [InlineData("  1. i\n")]
    [InlineData("  1.  i\n")]
    [InlineData("  1. i \n")]
    [InlineData("  1.  i \n")]
    [InlineData("  1.  i  \n")]

    [InlineData("   1. i\n")]
    [InlineData("   1.  i\n")]
    [InlineData("   1. i \n")]
    [InlineData("   1.  i \n")]
    [InlineData("   1.  i  \n")]

    [InlineData("1. i\n2. j")]
    [InlineData("1.  i\n2. j")]
    [InlineData("1. i \n2. j")]
    [InlineData("1.  i \n2. j")]
    [InlineData("1.  i  \n2. j")]

    [InlineData(" 1. i\n2. j")]
    [InlineData(" 1.  i\n2. j")]
    [InlineData(" 1. i \n2. j")]
    [InlineData(" 1.  i \n2. j")]
    [InlineData(" 1.  i  \n2. j")]

    [InlineData("  1. i\n2. j")]
    [InlineData("  1.  i\n2. j")]
    [InlineData("  1. i \n2. j")]
    [InlineData("  1.  i \n2. j")]
    [InlineData("  1.  i  \n2. j")]

    [InlineData("   1. i\n2. j")]
    [InlineData("   1.  i\n2. j")]
    [InlineData("   1. i \n2. j")]
    [InlineData("   1.  i \n2. j")]
    [InlineData("   1.  i  \n2. j")]

    [InlineData("1. i\n2. j\n")]
    [InlineData("1.  i\n2. j\n")]
    [InlineData("1. i \n2. j\n")]
    [InlineData("1.  i \n2. j\n")]
    [InlineData("1.  i  \n2. j\n")]

    [InlineData(" 1. i\n2. j\n")]
    [InlineData(" 1.  i\n2. j\n")]
    [InlineData(" 1. i \n2. j\n")]
    [InlineData(" 1.  i \n2. j\n")]
    [InlineData(" 1.  i  \n2. j\n")]

    [InlineData("  1. i\n2. j\n")]
    [InlineData("  1.  i\n2. j\n")]
    [InlineData("  1. i \n2. j\n")]
    [InlineData("  1.  i \n2. j\n")]
    [InlineData("  1.  i  \n2. j\n")]

    [InlineData("   1. i\n2. j\n")]
    [InlineData("   1.  i\n2. j\n")]
    [InlineData("   1. i \n2. j\n")]
    [InlineData("   1.  i \n2. j\n")]
    [InlineData("   1.  i  \n2. j\n")]

    [InlineData("1. i\n2. j\n3. k")]
    [InlineData("1. i\n2. j\n3. k\n")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("10. i")]
    [InlineData("11. i")]
    [InlineData("10. i\n12. i")]
    [InlineData("2. i\n3. i")]
    public void Test_MoreThenOneStart(string value)
    {
        RoundTrip(value);
    }


    [Theory]
    [InlineData("\n1. i")]
    [InlineData("\r1. i")]
    [InlineData("\r\n1. i")]

    [InlineData("\n1. i\n")]
    [InlineData("\r1. i\n")]
    [InlineData("\r\n1. i\n")]

    [InlineData("\n1. i\r")]
    [InlineData("\r1. i\r")]
    [InlineData("\r\n1. i\r")]

    [InlineData("\n1. i\r\n")]
    [InlineData("\r1. i\r\n")]
    [InlineData("\r\n1. i\r\n")]

    [InlineData("1. i\n2. i")]
    [InlineData("\n1. i\n2. i")]
    [InlineData("\r1. i\n2. i")]
    [InlineData("\r\n1. i\n2. i")]

    [InlineData("1. i\r2. i")]
    [InlineData("\n1. i\r2. i")]
    [InlineData("\r1. i\r2. i")]
    [InlineData("\r\n1. i\r2. i")]

    [InlineData("1. i\r\n2. i")]
    [InlineData("\n1. i\r\n2. i")]
    [InlineData("\r1. i\r\n2. i")]
    [InlineData("\r\n1. i\r\n2. i")]

    [InlineData("1. i\n2. i\n")]
    [InlineData("\n1. i\n2. i\n")]
    [InlineData("\r1. i\n2. i\n")]
    [InlineData("\r\n1. i\n2. i\n")]

    [InlineData("1. i\r2. i\r")]
    [InlineData("\n1. i\r2. i\r")]
    [InlineData("\r1. i\r2. i\r")]
    [InlineData("\r\n1. i\r2. i\r")]

    [InlineData("1. i\r\n2. i\r\n")]
    [InlineData("\n1. i\r\n2. i\r\n")]
    [InlineData("\r1. i\r\n2. i\r\n")]
    [InlineData("\r\n1. i\r\n2. i\r\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("1. i\n  1. i")]
    [InlineData("1. i\n  1. i\n")]
    [InlineData("1. i\n  1. i\n  2. i")]
    [InlineData("1. i\n  2. i\n  3. i")]

    [InlineData("1. i\n\t1. i")]
    [InlineData("1. i\n\t1. i\n2. i")]
    public void TestMultipleLevels(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("1.     c")]
    [InlineData("1.      c")]
    public void Test_IndentedCodeBlock(string value)
    {
        RoundTrip(value);
    }
}
