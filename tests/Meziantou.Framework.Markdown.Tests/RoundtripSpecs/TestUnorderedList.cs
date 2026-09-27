using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestUnorderedList
{
    // i = item
    [Theory]
    [InlineData("- i1")]
    [InlineData("- i1 ")]
    [InlineData("- i1\n")]
    [InlineData("- i1\n\n")]
    [InlineData("- i1\n- i2")]
    [InlineData("- i1\n    - i2")]
    [InlineData("- i1\n    - i1.1\n    - i1.2")]
    [InlineData("- i1 \n- i2 \n")]
    [InlineData("- i1  \n- i2  \n")]
    [InlineData(" - i1")]
    [InlineData("  - i1")]
    [InlineData("   - i1")]
    [InlineData("- i1\n\n- i1")]
    [InlineData("- i1\n\n\n- i1")]
    [InlineData("- i1\n    - i1.1\n        - i1.1.1\n")]

    [InlineData("-\ti1")]
    [InlineData("-\ti1\n-\ti2")]
    [InlineData("-\ti1\n-  i2\n-\ti3")]
    [InlineData("- 1.\n- 2.")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("- > q")]
    [InlineData(" - > q")]
    [InlineData("  - > q")]
    [InlineData("   - > q")]
    [InlineData("-  > q")]
    [InlineData(" -  > q")]
    [InlineData("  -  > q")]
    [InlineData("   -  > q")]
    [InlineData("-   > q")]
    [InlineData(" -   > q")]
    [InlineData("  -   > q")]
    [InlineData("   -   > q")]
    [InlineData("-    > q")]
    [InlineData(" -    > q")]
    [InlineData("  -    > q")]
    [InlineData("   -    > q")]
    [InlineData("   -    > q1\n   -    > q2")]
    public void TestBlockQuote(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("-     i1\n\np\n")] // TODO: listblock should render newline, apparently last paragraph of last listitem dont have newline
    [InlineData("-     i1\n\n\np\n")]
    [InlineData("- i1\n\np")]
    [InlineData("- i1\n\np\n")]
    public void TestParagraph(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("- i1\n\n---\n")]
    [InlineData("- i1\n\n\n---\n")]
    public void TestThematicBreak(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("-     c")] // 5
    [InlineData("-     c\n      c")] // 5, 6
    [InlineData(" -     c\n      c")] // 5, 6
    [InlineData(" -     c\n       c")] // 5, 7
    [InlineData("-      c\n      c")] // 6, 6
    [InlineData(" -      c\n      c")] // 6, 6
    [InlineData(" -      c\n       c")] // 6, 7
    public void TestIndentedCodeBlock(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("- ```a```")]
    [InlineData("- ```\n  a\n```")]
    [InlineData("- i1\n    - i1.1\n    ```\n    c\n    ```")]
    [InlineData("- i1\n    - i1.1\n    ```\nc\n```")]
    [InlineData("- i1\n    - i1.1\n    ```\nc\n```\n")]
    public void TestFencedCodeBlock(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("\n- i")]
    [InlineData("\r- i")]
    [InlineData("\r\n- i")]

    [InlineData("\n- i\n")]
    [InlineData("\r- i\n")]
    [InlineData("\r\n- i\n")]

    [InlineData("\n- i\r")]
    [InlineData("\r- i\r")]
    [InlineData("\r\n- i\r")]

    [InlineData("\n- i\r\n")]
    [InlineData("\r- i\r\n")]
    [InlineData("\r\n- i\r\n")]

    [InlineData("- i\n- j")]
    [InlineData("- i\r- j")]
    [InlineData("- i\r\n- j")]

    [InlineData("\n- i\n- j")]
    [InlineData("\n- i\r- j")]
    [InlineData("\n- i\r\n- j")]

    [InlineData("\r- i\n- j")]
    [InlineData("\r- i\r- j")]
    [InlineData("\r- i\r\n- j")]

    [InlineData("\r\n- i\n- j")]
    [InlineData("\r\n- i\r- j")]
    [InlineData("\r\n- i\r\n- j")]

    [InlineData("- i\n- j\n")]
    [InlineData("- i\r- j\n")]
    [InlineData("- i\r\n- j\n")]

    [InlineData("\n- i\n- j\n")]
    [InlineData("\n- i\r- j\n")]
    [InlineData("\n- i\r\n- j\n")]

    [InlineData("\r- i\n- j\n")]
    [InlineData("\r- i\r- j\n")]
    [InlineData("\r- i\r\n- j\n")]

    [InlineData("\r\n- i\n- j\n")]
    [InlineData("\r\n- i\r- j\n")]
    [InlineData("\r\n- i\r\n- j\n")]

    [InlineData("- i\n- j\r")]
    [InlineData("- i\r- j\r")]
    [InlineData("- i\r\n- j\r")]

    [InlineData("\n- i\n- j\r")]
    [InlineData("\n- i\r- j\r")]
    [InlineData("\n- i\r\n- j\r")]

    [InlineData("\r- i\n- j\r")]
    [InlineData("\r- i\r- j\r")]
    [InlineData("\r- i\r\n- j\r")]

    [InlineData("\r\n- i\n- j\r")]
    [InlineData("\r\n- i\r- j\r")]
    [InlineData("\r\n- i\r\n- j\r")]

    [InlineData("- i\n- j\r\n")]
    [InlineData("- i\r- j\r\n")]
    [InlineData("- i\r\n- j\r\n")]

    [InlineData("\n- i\n- j\r\n")]
    [InlineData("\n- i\r- j\r\n")]
    [InlineData("\n- i\r\n- j\r\n")]

    [InlineData("\r- i\n- j\r\n")]
    [InlineData("\r- i\r- j\r\n")]
    [InlineData("\r- i\r\n- j\r\n")]

    [InlineData("\r\n- i\n- j\r\n")]
    [InlineData("\r\n- i\r- j\r\n")]
    [InlineData("\r\n- i\r\n- j\r\n")]

    [InlineData("- i\n")]
    [InlineData("- i\n\n")]
    [InlineData("- i\n\n\n")]
    [InlineData("- i\n\n\n\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("a\n\n-")]
    [InlineData("> a\n-")]
    [InlineData("a\n*     ")]
    [InlineData("- \n     a")]
    [InlineData("- \n  \n  a")]
    [InlineData("-\n  foo\n\n  bar")]
    [InlineData("- foo\n\n  ***")]
    [InlineData("*\n  ***")]
    public void TestItemStartingWithABlankLineOrContainingAThematicBreak(string value)
    {
        RoundTrip(value);
    }
}
