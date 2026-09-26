namespace Meziantou.Framework.Markdown.Tests;

public class TestCodeInline
{
    [Fact]
    public void UnpairedCodeInlineWithTrailingChars()
    {
        TestParser.TestSpec("*`\n\f", "<p>*`</p>");
    }
}
