using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestAtxHeading
{
    [Theory]
    [InlineData("# h")]
    [InlineData("# h ")]
    [InlineData("# h\n#h")]
    [InlineData("# h\n #h")]
    [InlineData("# h\n # h")]
    [InlineData("# h\n # h ")]
    [InlineData(" #  h   \n    #     h      ")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("\n# h\n\np")]
    [InlineData("\n# h\n\np\n")]
    [InlineData("\n# h\n\np\n\n")]
    [InlineData("\n\n# h\n\np\n\n")]
    [InlineData("\n\n# h\np\n\n")]
    public void TestParagraph(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("\n# h")]
    [InlineData("\n# h\n")]
    [InlineData("\n# h\r")]
    [InlineData("\n# h\r\n")]

    [InlineData("\r# h")]
    [InlineData("\r# h\n")]
    [InlineData("\r# h\r")]
    [InlineData("\r# h\r\n")]

    [InlineData("\r\n# h")]
    [InlineData("\r\n# h\n")]
    [InlineData("\r\n# h\r")]
    [InlineData("\r\n# h\r\n")]

    [InlineData("# h\n\n ")]
    [InlineData("# h\n\n  ")]
    [InlineData("# h\n\n   ")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }
}
