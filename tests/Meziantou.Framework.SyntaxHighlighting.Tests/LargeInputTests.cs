namespace Meziantou.Framework.SyntaxHighlighting.Tests;

/// <summary>
/// Every other test in this project highlights a snippet of a few dozen bytes, so none of them
/// notice how the tokenizer scales. This one does: it highlights a document of a realistic size
/// and fails if that takes an unreasonable amount of time. The budget is deliberately loose — the
/// point is to catch a return to super-linear scanning, not to measure throughput.
/// </summary>
public sealed class LargeInputTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("bash")]
    [InlineData("bnf")]
    [InlineData("cpp")]
    [InlineData("csharp")]
    [InlineData("css")]
    [InlineData("dockerfile")]
    [InlineData("dos")]
    [InlineData("fsharp")]
    [InlineData("graphql")]
    [InlineData("html")]
    [InlineData("http")]
    [InlineData("ini")]
    [InlineData("javascript")]
    [InlineData("json")]
    [InlineData("less")]
    [InlineData("markdown")]
    [InlineData("msil")]
    [InlineData("nginx")]
    [InlineData("php")]
    [InlineData("powershell")]
    [InlineData("razor")]
    [InlineData("scss")]
    [InlineData("sql")]
    [InlineData("typescript")]
    [InlineData("urlencoded")]
    [InlineData("vbnet")]
    [InlineData("x86asm")]
    [InlineData("xml")]
    [InlineData("yaml")]
    public async Task Highlight_LargeDocument_CompletesInReasonableTime(string language)
    {
        const string Line = "public int Method(int a) { return a + 1; } /* note */ \"text\" 'c' <T> @name #tag\n";
        var code = string.Concat(Enumerable.Repeat(Line, 800));

        var highlight = Task.Run(() => SyntaxHighlighter.Highlight(code, language));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of '{language}' did not finish within {Budget.TotalSeconds:F0}s.");
    }
}
