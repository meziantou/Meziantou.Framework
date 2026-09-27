using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestCodeInline
{
    [Theory]
    [InlineData("``")]
    [InlineData(" ``")]
    [InlineData("`` ")]
    [InlineData(" `` ")]

    [InlineData("`c`")]
    [InlineData(" `c`")]
    [InlineData("`c` ")]
    [InlineData(" `c` ")]

    [InlineData("` c`")]
    [InlineData(" ` c`")]
    [InlineData("` c` ")]
    [InlineData(" ` c` ")]

    [InlineData("`c `")]
    [InlineData(" `c `")]
    [InlineData("`c ` ")]
    [InlineData(" `c ` ")]

    [InlineData("`c``")] // 1, 2
    [InlineData("``c`")] // 2, 1
    [InlineData("``c``")] // 2, 2

    [InlineData("```c``")] // 2, 3
    [InlineData("``c```")] // 3, 2
    [InlineData("```c```")] // 3, 3

    [InlineData("```c````")] // 3, 4
    [InlineData("````c```")] // 4, 3
    [InlineData("````c````")] // 4, 4

    [InlineData("```a``` p")]
    [InlineData("```a`b`c```")]
    [InlineData("```a``` p\n```a``` p")]

    [InlineData("` a `")]
    [InlineData(" ` a `")]
    [InlineData("` a ` ")]
    [InlineData(" ` a ` ")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("p `a` p")]
    [InlineData("p ``a`` p")]
    [InlineData("p ```a``` p")]
    [InlineData("p\n\n```a``` p")]
    public void TestParagraph(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("`\na\n`")]
    [InlineData("`\na\r`")]
    [InlineData("`\na\r\n`")]
    [InlineData("`\ra\r`")]
    [InlineData("`\ra\n`")]
    [InlineData("`\ra\r\n`")]
    [InlineData("`\r\na\n`")]
    [InlineData("`\r\na\r`")]
    [InlineData("`\r\na\r\n`")]
    public void Test_Newlines(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> `a\n> b`\n")]
    [InlineData("> `a\r> b`\r")]
    [InlineData("> ``a\n> b\n> c``\n")]
    [InlineData(">> x `a\n>> b`\n")]
    [InlineData("> - `a\n>   b`\n")]
    public void TestSpanningLinesInContainer(string value)
    {
        RoundTrip(value);
    }
}
