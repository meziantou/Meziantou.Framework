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
    [MemberData(nameof(Grammars), MemberType = typeof(Helper))]
    public async Task Highlight_LargeDocument_CompletesInReasonableTime(string language)
    {
        const string Line = "public int Method(int a) { return a + 1; } /* note */ \"text\" 'c' <T> @name #tag\n";
        var code = string.Concat(Enumerable.Repeat(Line, 800));

        var isFallback = false;
        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, language, out isFallback));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of '{language}' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.False(isFallback, $"Highlighting {code.Length} characters of '{language}' was abandoned.");
    }

    // Modes whose end must repeat a delimiter captured at begin (heredocs, raw strings) and the JSX tag guard used to
    // rescan the rest of the document after every token or candidate inside them. At these sizes the quadratic
    // versions took one to several minutes; the budget is loose because the linear ones still take seconds on a
    // heavily loaded machine.
    [Theory]
    [InlineData("php")]
    [InlineData("csharp")]
    [InlineData("typescript")]
    public async Task Highlight_DocumentThatUsedToBeRescannedPerToken_CompletesInReasonableTime(string language)
    {
        var code = language switch
        {
            "php" => "<?php\n$a = <<<EOT\n" + string.Concat(Enumerable.Repeat("  $name some words here {$x}\n", 16_000)) + "EOT;\nclass C { }\n",
            "csharp" => "var s = $\"\"\"\"\n" + string.Concat(Enumerable.Repeat("\"\"\" {x}\n", 64_000)) + "\"\"\"\";\nclass C { }\n",
            "typescript" => "x = " + string.Concat(Enumerable.Repeat("<a>", 40_000)) + " " + string.Concat(Enumerable.Repeat("</b", 40_000)) + ";\nclass C { }\n",
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };

        var budget = TimeSpan.FromSeconds(30);
        var highlight = Task.Run(() => SyntaxHighlighter.Highlight(code, language));
        var finished = await Task.WhenAny(highlight, Task.Delay(budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of '{language}' did not finish within {budget.TotalSeconds:F0}s.");
        var html = await highlight;
        Assert.Contains("<span class=\"hljs-keyword\">class</span>", html[^100..], ignoreCase: false);
    }
}
