using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class TestFencedCodeBlocks
{
    [Theory]
    [InlineData("c#", "c#", "")]
    [InlineData("C#", "C#", "")]
    [InlineData(" c#", "c#", "")]
    [InlineData(" c# ", "c#", "")]
    [InlineData(" \tc# ", "c#", "")]
    [InlineData("\t c# \t", "c#", "")]
    [InlineData(" c# foo", "c#", "foo")]
    [InlineData(" c# \t  fOo \t", "c#", "fOo")]
    [InlineData("in\\%fo arg\\%ument", "in%fo", "arg%ument")]
    [InlineData("info&#9; arg&acute;ument", "info\t", "arg\u00B4ument")]
    public void TestInfoAndArguments(string infoString, string expectedInfo, string expectedArguments)
    {
        Test('`');
        Test('~');

        void Test(char fencedChar)
        {
            const string Contents = "Foo\nBar\n";

            var fence = new string(fencedChar, 3);
            string markdownText = $"{fence}{infoString}\n{Contents}\n{fence}\n";

            MarkdownDocument document = MarkdownConverter.Parse(markdownText);

            FencedCodeBlock codeBlock = document.Descendants<FencedCodeBlock>().Single();

            Assert.Equal(fencedChar, codeBlock.FencedChar);
            Assert.Equal(3, codeBlock.OpeningFencedCharCount);
            Assert.Equal(3, codeBlock.ClosingFencedCharCount);
            Assert.Equal(expectedInfo, codeBlock.Info);
            Assert.Equal(expectedArguments, codeBlock.Arguments);
            Assert.Equal(Contents, codeBlock.Lines.ToString());
        }
    }
}
