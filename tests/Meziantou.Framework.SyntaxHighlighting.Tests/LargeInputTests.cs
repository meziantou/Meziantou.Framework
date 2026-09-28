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
    [InlineData("ruby")]
    [InlineData("perl")]
    public async Task Highlight_DocumentThatUsedToBeRescannedPerToken_CompletesInReasonableTime(string language)
    {
        var code = language switch
        {
            "php" => "<?php\n$a = <<<EOT\n" + string.Concat(Enumerable.Repeat("  $name some words here {$x}\n", 16_000)) + "EOT;\nclass C { }\n",
            "csharp" => "var s = $\"\"\"\"\n" + string.Concat(Enumerable.Repeat("\"\"\" {x}\n", 64_000)) + "\"\"\"\";\nclass C { }\n",
            "ruby" => "a = <<~EOS\n" + string.Concat(Enumerable.Repeat("  #{name} some words here \\t\n", 16_000)) + "EOS\nclass C\nend\n",
            "perl" => "print <<\"EOS\";\n" + string.Concat(Enumerable.Repeat("  $name some words here @{[ $x ]}\n", 16_000)) + "EOS\nclass Foo;\n",
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

    // Before a LaTeX3 macro name could only start right after a backslash, looking for the next one scanned a run of letters
    // from each of its positions to the end of the run (half a minute for this document).
    [Fact]
    public async Task Highlight_LatexLongLetterRun_CompletesInReasonableTime()
    {
        var code = "\\x " + string.Concat(Enumerable.Repeat("aa_", 40_000)) + "\n\\end";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "latex", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'latex' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-keyword\">\\end</span>", await highlight, StringComparison.Ordinal);
    }

    // A properties key used to be matched from each of its positions, which is quadratic on a long key made of escapes
    // (a minute and a half for 60,000 backslashes).
    [Theory]
    [InlineData("\\")]
    [InlineData("a\\ ")]
    [InlineData("a")]
    public async Task Highlight_PropertiesLongKey_CompletesInReasonableTime(string keyPart)
    {
        var code = string.Concat(Enumerable.Repeat(keyPart, 60_000 / keyPart.Length)) + "\nkey = value";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "properties", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'properties' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-attr\">key</span> = <span class=\"hljs-string\">value</span>", await highlight, StringComparison.Ordinal);
    }
}
