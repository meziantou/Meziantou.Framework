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
    [InlineData("pgsql")]
    public async Task Highlight_DocumentThatUsedToBeRescannedPerToken_CompletesInReasonableTime(string language)
    {
        var code = language switch
        {
            "php" => "<?php\n$a = <<<EOT\n" + string.Concat(Enumerable.Repeat("  $name some words here {$x}\n", 16_000)) + "EOT;\nclass C { }\n",
            "csharp" => "var s = $\"\"\"\"\n" + string.Concat(Enumerable.Repeat("\"\"\" {x}\n", 64_000)) + "\"\"\"\";\nclass C { }\n",
            "ruby" => "a = <<~EOS\n" + string.Concat(Enumerable.Repeat("  #{name} some words here \\t\n", 16_000)) + "EOS\nclass C\nend\n",
            "perl" => "print <<\"EOS\";\n" + string.Concat(Enumerable.Repeat("  $name some words here @{[ $x ]}\n", 16_000)) + "EOS\nclass Foo;\n",
            "pgsql" => "CREATE FUNCTION f() RETURNS int AS $a$\n" + string.Concat(Enumerable.Repeat("  my $b = \"$c$ some words $$ here\";\n", 16_000)) + "$a$ LANGUAGE plperl;\nclass C { }\n",
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

    // highlight.js matches an LLVM label with `^\s*[a-z]+:`, which rescans a run of blank lines from each of its line starts
    // (far over the budget for this document).
    [Fact]
    public async Task Highlight_LlvmLongBlankRun_CompletesInReasonableTime()
    {
        var code = "ret void\n" + new string('\n', 400_000) + "  ret void";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "llvm", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'llvm' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-keyword\">ret</span> <span class=\"hljs-type\">void</span>", await highlight, StringComparison.Ordinal);
    }

    // After each `(`, the parameter list of an anonymous CoffeeScript function used to be looked for from every `(` of the
    // rest of the line, each time scanning to the end of the line (24 seconds for this document).
    [Fact]
    public async Task Highlight_CoffeeScriptLongParenthesisRun_CompletesInReasonableTime()
    {
        var code = "x = " + new string('(', 100_000) + "\nyes";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "coffeescript", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'coffeescript' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-literal\">yes</span>", await highlight, StringComparison.Ordinal);
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

    // A Handlebars hash parameter (`key=value`) used to be matched from each position of an identifier, which is
    // quadratic on a long identifier that is not followed by `=` (2.5 seconds for 60,000 characters).
    [Fact]
    public async Task Highlight_HandlebarsLongIdentifier_CompletesInReasonableTime()
    {
        var code = "{{helper " + new string('a', 150_000) + " b}}";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "handlebars", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'handlebars' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith(" b}}</span>", await highlight, StringComparison.Ordinal);
    }

    // The indentation of a Haml comment line used to be captured before checking that a comment follows, which rescanned
    // the indentation from each of its positions (about 50 seconds for this document).
    [Fact]
    public async Task Highlight_HamlLongIndentation_CompletesInReasonableTime()
    {
        var code = new string(' ', 200_000) + "-# comment";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "haml", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'haml' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("-# comment</span>", await highlight, StringComparison.Ordinal);
    }

    // A number pattern that fails identically from every position of a long run of digits, and an identifier that is
    // matched over the whole rest of a run after each number that wins against it at the same position, used to make
    // these documents quadratic (more than a minute for the longest ones).
    [Theory]
    [InlineData("scheme", "digits")]
    [InlineData("scheme", "numbers")]
    [InlineData("lisp", "numbers")]
    public async Task Highlight_LispFamilyLongRun_CompletesInReasonableTime(string language, string content)
    {
        var code = content switch
        {
            "digits" => "(f " + new string('1', 100_000) + ")\n(define x 1)",
            "numbers" => "(f " + string.Concat(Enumerable.Repeat("1-", 300_000)) + ")\n(define x 1)",
            _ => throw new ArgumentOutOfRangeException(nameof(content)),
        };

        var isFallback = false;
        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, language, out isFallback));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of '{language}' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.False(isFallback, $"Highlighting {code.Length} characters of '{language}' was abandoned.");
    }

    // The guard against grammars that stop making progress used to count the hits of every run of the document, but
    // compared the count with a position in the current fragment, so a document with many embedded fragments was
    // abandoned (plain text).
    [Theory]
    [InlineData("html")]
    [InlineData("pgsql")]
    public void Highlight_ManyEmbeddedFragments_IsNotAbandoned(string language)
    {
        var code = language switch
        {
            "html" => string.Concat(Enumerable.Repeat("<script>var a = 1;</script>\n", 20_000)),
            "pgsql" => string.Concat(Enumerable.Repeat("CREATE FUNCTION f() RETURNS int AS $$ BEGIN RETURN 1; END $$ LANGUAGE plpgsql;\n", 10_000)),
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };

        HighlightWithFallbackDetection(code, language, out var isFallback);

        Assert.False(isFallback, $"Highlighting {code.Length} characters of '{language}' was abandoned.");
    }
}
