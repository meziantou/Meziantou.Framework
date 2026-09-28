namespace Meziantou.Framework.SyntaxHighlighting.Tests;

/// <summary>
/// Every other test in this project highlights a snippet of a few dozen bytes, so none of them
/// notice how the tokenizer scales. This one does: it highlights a document of a realistic size
/// and fails if that takes an unreasonable amount of time. The budget is deliberately loose — the
/// point is to catch a return to super-linear scanning, not to measure throughput.
/// </summary>
public sealed class LargeInputTests
{
    // The budgets are CPU time of the highlighting thread: on a busy CI runner, most of the wall-clock time of a test is spent
    // waiting for a core, or for the thread pool to run it at all. A highlighting that never ends still fails, after the
    // wall-clock timeout.
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HangTimeout = TimeSpan.FromMinutes(2);

    // A pattern that is tried from each position of a run and scans to the end of the run makes a grammar quadratic, which
    // a single realistic document does not show. A run of one delimiter is where that happens most, so a run of 40,000
    // characters is compared with four runs of 10,000, which also catches a quadratic grammar that is still fast at one
    // size (the long run then takes four times as long, a linear one as long). The time is the CPU time of the thread, so
    // the other tests do not count, and both measurements are long enough for Windows' 15.6 ms clock. A garbage
    // collection can still land in one measurement, so a run that looks quadratic is measured again before failing. The
    // theory runs alone because it keeps a core busy for a long time.
    [Theory(DisableParallelization = true)]
    [MemberData(nameof(Grammars), MemberType = typeof(Helper))]
    public void Highlight_LongRunOfOneCharacter_ScalesLinearly(string language)
    {
        const string Characters = "\"'`/\\*#-=+<>{}[]()$@%:;,.| \t\na1";
        const int SmallSize = 10_000;
        const int LargeSize = 4 * SmallSize;

        foreach (var character in Characters)
        {
            var smallRun = new string(character, SmallSize);
            var largeRun = new string(character, LargeSize);
            HighlightWithFallbackDetection(smallRun, language, out _);

            var smallRuns = MeasureFastest(smallRun, language, repetitions: 4, attempts: 1);
            var large = MeasureFastest(largeRun, language, repetitions: 1, attempts: 1);
            if (!ScalesLinearly(smallRuns, large))
            {
                smallRuns = MeasureFastest(smallRun, language, repetitions: 4, attempts: 3);
                large = MeasureFastest(largeRun, language, repetitions: 1, attempts: 3);
            }

            Assert.True(ScalesLinearly(smallRuns, large), $"Highlighting a run of {LargeSize} '{character}' in '{language}' took {large.TotalMilliseconds:F0} ms of CPU time, and four runs of {SmallSize} took {smallRuns.TotalMilliseconds:F0} ms.");
        }

        static bool ScalesLinearly(TimeSpan smallRuns, TimeSpan large) => large <= (smallRuns * 2) + TimeSpan.FromMilliseconds(50);

        static TimeSpan MeasureFastest(string code, string language, int repetitions, int attempts)
        {
            var fastest = TimeSpan.MaxValue;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var stopwatch = ThreadCpuStopwatch.StartNew();
                for (var i = 0; i < repetitions; i++)
                {
                    HighlightWithFallbackDetection(code, language, out _);
                }

                var elapsed = stopwatch.Elapsed;
                if (elapsed < fastest)
                {
                    fastest = elapsed;
                }
            }

            return fastest;
        }
    }

    [Theory]
    [MemberData(nameof(Grammars), MemberType = typeof(Helper))]
    public async Task Highlight_LargeDocument_CompletesInReasonableTime(string language)
    {
        const string Line = "public int Method(int a) { return a + 1; } /* note */ \"text\" 'c' <T> @name #tag\n";
        var code = string.Concat(Enumerable.Repeat(Line, 800));

        var isFallback = false;
        await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, language, out isFallback), $"Highlighting {code.Length} characters of '{language}'");

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
        var html = await HighlightWithinBudget(() => SyntaxHighlighter.Highlight(code, language), $"Highlighting {code.Length} characters of '{language}'", budget);

        Assert.Contains("<span class=\"hljs-keyword\">class</span>", html[^100..], ignoreCase: false);
    }

    // Before a LaTeX3 macro name could only start right after a backslash, looking for the next one scanned a run of letters
    // from each of its positions to the end of the run (half a minute for this document).
    [Fact]
    public async Task Highlight_LatexLongLetterRun_CompletesInReasonableTime()
    {
        var code = "\\x " + string.Concat(Enumerable.Repeat("aa_", 40_000)) + "\n\\end";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "latex", out _), $"Highlighting {code.Length} characters of 'latex'");

        Assert.EndsWith("<span class=\"hljs-keyword\">\\end</span>", html, StringComparison.Ordinal);
    }

    // highlight.js matches an LLVM label with `^\s*[a-z]+:`, which rescans a run of blank lines from each of its line starts
    // (far over the budget for this document).
    [Fact]
    public async Task Highlight_LlvmLongBlankRun_CompletesInReasonableTime()
    {
        var code = "ret void\n" + new string('\n', 400_000) + "  ret void";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "llvm", out _), $"Highlighting {code.Length} characters of 'llvm'");

        Assert.EndsWith("<span class=\"hljs-keyword\">ret</span> <span class=\"hljs-type\">void</span>", html, StringComparison.Ordinal);
    }

    // After each `(`, the parameter list of an anonymous CoffeeScript function used to be looked for from every `(` of the
    // rest of the line, each time scanning to the end of the line (24 seconds for this document).
    [Fact]
    public async Task Highlight_CoffeeScriptLongParenthesisRun_CompletesInReasonableTime()
    {
        var code = "x = " + new string('(', 100_000) + "\nyes";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "coffeescript", out _), $"Highlighting {code.Length} characters of 'coffeescript'");

        Assert.EndsWith("<span class=\"hljs-literal\">yes</span>", html, StringComparison.Ordinal);
    }

    // A CoffeeScript regular expression literal used to look for its closing `/` from each `/` of a line up to the end of
    // the line (more than a minute for 100,000 characters).
    [Theory]
    [InlineData("x = a", "/a")]
    [InlineData("x = /", "a/")]
    [InlineData("", "a/b")]
    [InlineData("x = ", "/a 0b1")]
    [InlineData("x = ", "/a\\")]
    public async Task Highlight_CoffeeScriptLongLineOfSlashes_CompletesInReasonableTime(string prefix, string part)
    {
        var code = prefix + string.Concat(Enumerable.Repeat(part, 100_000 / part.Length)) + "\nyes";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "coffeescript", out isFallback), $"Highlighting {code.Length} characters of 'coffeescript'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'coffeescript' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-literal\">yes</span>", html, StringComparison.Ordinal);
    }

    // A Verilog parameter list (`#(…)`) used to be looked for from each `#(` of a line that does not close it up to the end
    // of the line (40 seconds for 100,000 characters).
    [Theory]
    [InlineData("#(")]
    [InlineData("#(a")]
    [InlineData("#(1")]
    [InlineData("#(.a")]
    [InlineData("#((")]
    public async Task Highlight_VerilogLongLineOfUnclosedParameters_CompletesInReasonableTime(string part)
    {
        var code = string.Concat(Enumerable.Repeat(part, 100_000 / part.Length)) + "\nendmodule";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "verilog", out isFallback), $"Highlighting {code.Length} characters of 'verilog'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'verilog' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-keyword\">endmodule</span>", html, StringComparison.Ordinal);
    }

    // The whitespace after the `=` of a Zig container declaration (`const Point = struct`) used to be split in every
    // possible way between two runs of whitespace before failing (4 seconds for 100,000 characters).
    [Theory]
    [InlineData("var a = ", " ")]
    [InlineData("const a =", " ")]
    [InlineData("var a = ", "\n")]
    public async Task Highlight_ZigLongWhitespaceAfterAssignment_CompletesInReasonableTime(string prefix, string whitespace)
    {
        var code = prefix + string.Concat(Enumerable.Repeat(whitespace, 200_000)) + "1;";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "zig", out isFallback), $"Highlighting {code.Length} characters of 'zig'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'zig' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-number\">1</span>;", html, StringComparison.Ordinal);
    }

    // An Erlang triple-quoted string used to be looked for from each quote of a run, each time consuming the rest of the
    // run and then scanning the rest of the document for a closing delimiter (20 seconds for 40,000 quotes).
    [Theory]
    [InlineData("quotes")]
    [InlineData("sigil")]
    [InlineData("decreasing")]
    public async Task Highlight_ErlangLongRunOfQuotes_CompletesInReasonableTime(string content)
    {
        var code = content switch
        {
            "quotes" => new string('"', 100_000),
            "sigil" => "~s" + new string('"', 100_000),
            "decreasing" => string.Concat(Enumerable.Range(3, 440).Reverse().Select(length => new string('"', length) + "a")),
            _ => throw new ArgumentOutOfRangeException(nameof(content)),
        } + "\n-module(m).";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "erlang", out isFallback), $"Highlighting {code.Length} characters of 'erlang'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'erlang' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-keyword\">-module</span><span class=\"hljs-params\">(m)</span>.", html, StringComparison.Ordinal);
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

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "properties", out _), $"Highlighting {code.Length} characters of 'properties'");

        Assert.EndsWith("<span class=\"hljs-attr\">key</span> = <span class=\"hljs-string\">value</span>", html, StringComparison.Ordinal);
    }

    // A Handlebars hash parameter (`key=value`) used to be matched from each position of an identifier, which is
    // quadratic on a long identifier that is not followed by `=` (2.5 seconds for 60,000 characters).
    [Fact]
    public async Task Highlight_HandlebarsLongIdentifier_CompletesInReasonableTime()
    {
        var code = "{{helper " + new string('a', 150_000) + " b}}";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "handlebars", out _), $"Highlighting {code.Length} characters of 'handlebars'");

        Assert.EndsWith(" b}}</span>", html, StringComparison.Ordinal);
    }

    // A Svelte rune used to be rejected by a lookbehind that scanned back to the start of its line for a `//`, which is
    // quadratic on a long line of runes (about 10 seconds for this document).
    [Fact]
    public async Task Highlight_SvelteLongLineOfRunes_CompletesInReasonableTime()
    {
        var code = "<script>\n" + string.Concat(Enumerable.Repeat("$state(0);", 20_000));

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "svelte", out _), $"Highlighting {code.Length} characters of 'svelte'");

        Assert.EndsWith("<span class=\"hljs-built_in\">$state</span><span class=\"language-javascript\">(<span class=\"hljs-number\">0</span>);</span>", html, StringComparison.Ordinal);
    }

    // An unclosed dynamic argument of a Vue directive (`:[key`) used to be scanned to the end of the attribute name from
    // each `:[` of the name (about 8 seconds for this document).
    [Fact]
    public async Task Highlight_VueLongRunOfUnclosedDynamicArguments_CompletesInReasonableTime()
    {
        var code = "<div " + string.Concat(Enumerable.Repeat(":[", 100_000)) + ">\n<p>";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "vue", out _), $"Highlighting {code.Length} characters of 'vue'");

        Assert.EndsWith("&gt;</span>\n<span class=\"hljs-tag\">&lt;<span class=\"hljs-name\">p</span>&gt;</span>", html, StringComparison.Ordinal);
    }

    // A Handlebars `[ abc ]` segment that is not closed used to be scanned to the end of the document from each `[` that
    // follows it, in the same mustache or in a later one (15, 15 and 24 seconds for these documents).
    [Theory]
    [InlineData("{{x ", "[a", "}}", 1_000_000)]
    [InlineData("{{x ", "a.[a", "}}", 1_000_000)]
    [InlineData("", "{{x [ [ [ [ [ [ [}}\n", "", 1_400_000)]
    public async Task Highlight_HandlebarsUnclosedBracketSegments_CompletesInReasonableTime(string prefix, string part, string suffix, int length)
    {
        var code = prefix + string.Concat(Enumerable.Repeat(part, length / part.Length)) + suffix + "\n{{#if a}}";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "handlebars", out _), $"Highlighting {code.Length} characters of 'handlebars'");

        Assert.EndsWith("{{#<span class=\"hljs-name\"><span class=\"hljs-built_in\">if</span></span> a}}</span>", html, StringComparison.Ordinal);
    }

    // The indentation of a Haml comment line used to be captured before checking that a comment follows, which rescanned
    // the indentation from each of its positions (about 50 seconds for this document).
    [Fact]
    public async Task Highlight_HamlLongIndentation_CompletesInReasonableTime()
    {
        var code = new string(' ', 200_000) + "-# comment";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "haml", out _), $"Highlighting {code.Length} characters of 'haml'");

        Assert.EndsWith("-# comment</span>", html, StringComparison.Ordinal);
    }

    // The end of a Haml attribute hash (`}` after optional whitespace, or the end of a line that does not end with a
    // comma) used to be looked for by scanning a run of whitespace from each of its positions (30 seconds for this
    // document).
    [Fact]
    public async Task Highlight_HamlLongWhitespaceInAttributeHash_CompletesInReasonableTime()
    {
        var code = "%a{x" + new string(' ', 100_000) + "y\n%p text";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "haml", out _), $"Highlighting {code.Length} characters of 'haml'");

        Assert.EndsWith("<span class=\"hljs-tag\">%<span class=\"hljs-selector-tag\">p</span></span> text", html, StringComparison.Ordinal);
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
            "numbers" => "(f " + string.Concat(Enumerable.Repeat("1-", 200_000)) + ")\n(define x 1)",
            _ => throw new ArgumentOutOfRangeException(nameof(content)),
        };

        var isFallback = false;
        await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, language, out isFallback), $"Highlighting {code.Length} characters of '{language}'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of '{language}' was abandoned.");
    }

    // A Nix path used to be matched from each `/` of a run of path pieces, which is quadratic on a long run that is not
    // followed by a whitespace or a `;` (half a minute to more than a minute for these documents).
    [Theory]
    [InlineData("a/")]
    [InlineData("./")]
    public async Task Highlight_NixLongPathRun_CompletesInReasonableTime(string piece)
    {
        var code = string.Concat(Enumerable.Repeat(piece, 40_000)) + ")\nx = ./y;";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "nix", out _), $"Highlighting {code.Length} characters of 'nix'");

        Assert.EndsWith("<span class=\"hljs-symbol\">./y</span>;", html, StringComparison.Ordinal);
    }

    // An AWK `${…}` variable used to be matched from each `${` of a line without `}` up to the end of the line (twenty
    // seconds for this document).
    [Fact]
    public async Task Highlight_AwkLongLineOfUnclosedVariables_CompletesInReasonableTime()
    {
        var code = string.Concat(Enumerable.Repeat("${", 40_000)) + "\nBEGIN";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "awk", out _), $"Highlighting {code.Length} characters of 'awk'");

        Assert.EndsWith("<span class=\"hljs-keyword\">BEGIN</span>", html, StringComparison.Ordinal);
    }

    // A TOML key that is not followed by `=` must not be rescanned from each of its characters or dotted segments.
    [Theory]
    [InlineData("a")]
    [InlineData("a.")]
    [InlineData("a . ")]
    [InlineData("\"a\".")]
    [InlineData("\\\"a")]
    public async Task Highlight_TomlLongKey_CompletesInReasonableTime(string keyPart)
    {
        var code = string.Concat(Enumerable.Repeat(keyPart, 60_000 / keyPart.Length)) + "\nkey = 1";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "toml", out _), $"Highlighting {code.Length} characters of 'toml'");

        Assert.EndsWith("<span class=\"hljs-attr\">key</span> = <span class=\"hljs-number\">1</span>", html, StringComparison.Ordinal);
    }

    // A Mermaid link text (`-- text -->`) is only looked for from the first opening of a line, and the backward scan that
    // finds that opening only runs from an opening: either mistake makes a long line quadratic.
    [Theory]
    [InlineData("-")]
    [InlineData("- ")]
    [InlineData("A-->")]
    [InlineData("-- a ")]
    public async Task Highlight_MermaidLongLine_CompletesInReasonableTime(string part)
    {
        var code = "flowchart LR\n" + string.Concat(Enumerable.Repeat(part, 60_000 / part.Length)) + "\nA --> B";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "mermaid", out _), $"Highlighting {code.Length} characters of 'mermaid'");

        Assert.EndsWith("A <span class=\"hljs-operator\">--&gt;</span> B", html, StringComparison.Ordinal);
    }

    // The end of an unterminated Mermaid directive (a line made of words only) used to let a word be split anywhere, so a
    // line of letters that is not followed by the end of the line took exponential time (hours for 40 letters).
    [Theory]
    [InlineData("a", 40)]
    [InlineData("a", 60_000)]
    [InlineData(" ", 60_000)]
    [InlineData("a ", 60_000)]
    public async Task Highlight_MermaidUnterminatedDirective_CompletesInReasonableTime(string part, int length)
    {
        var code = "%%{init: {\"theme\": \"dark\"\n" + string.Concat(Enumerable.Repeat(part, length / part.Length)) + "!\nflowchart LR\nA --> B";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "mermaid", out _), $"Highlighting {code.Length} characters of 'mermaid'");

        Assert.EndsWith("A <span class=\"hljs-operator\">--&gt;</span> B", html, StringComparison.Ordinal);
    }

    // An AsciiDoc strong, emphasis or smart quote mark used to scan the rest of its line or paragraph for a closing mark
    // from each unclosed mark (8 to 40 seconds for the single-line documents).
    [Theory]
    [InlineData(" *a")]
    [InlineData(" * ")]
    [InlineData(" **a")]
    [InlineData(" __a")]
    [InlineData(" `a`")]
    [InlineData("x *a\n")]
    public async Task Highlight_AsciiDocUnclosedMarks_CompletesInReasonableTime(string part)
    {
        var code = string.Concat(Enumerable.Repeat(part, 60_000 / part.Length)) + "\n\n*end*";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "asciidoc", out _), $"Highlighting {code.Length} characters of 'asciidoc'");

        Assert.EndsWith("<span class=\"hljs-strong\">*end*</span>", html, StringComparison.Ordinal);
    }

    // An AsciiDoc link or image macro used to be looked for from each macro prefix of a run of target characters, each
    // time scanning to the end of the run (0.7 seconds for 100,000 characters, about 20 seconds for this document).
    [Theory]
    [InlineData("http://a")]
    [InlineData("http://a,")]
    [InlineData("file:/")]
    [InlineData("image:x")]
    [InlineData("link:http:")]
    [InlineData("|http://a")]
    [InlineData("http://a[b")]
    public async Task Highlight_AsciiDocLongRunOfMacroPrefixes_CompletesInReasonableTime(string part)
    {
        var code = string.Concat(Enumerable.Repeat(part, 600_000 / part.Length)) + "\n\nhttps://example.com[end]";

        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "asciidoc", out _), $"Highlighting {code.Length} characters of 'asciidoc'");

        Assert.EndsWith("<span class=\"hljs-link\">https://example.com</span>[<span class=\"hljs-string\">end</span>]", html, StringComparison.Ordinal);
    }

    // The Haskell operator that ends with dashes (`--+` followed by a symbol) used to be looked for from each dash of a run,
    // each time scanning to the end of the run (50 seconds for 100,000 dashes).
    [Theory]
    [InlineData("-")]
    [InlineData("-- ")]
    [InlineData("---a")]
    public async Task Highlight_HaskellLongDashRun_CompletesInReasonableTime(string part)
    {
        var code = "x = 1\n" + string.Concat(Enumerable.Repeat(part, 150_000 / part.Length)) + "\nmain = 1";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "haskell", out isFallback), $"Highlighting {code.Length} characters of 'haskell'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'haskell' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-title\">main</span> = <span class=\"hljs-number\">1</span>", html, StringComparison.Ordinal);
    }

    // The Clojure ratio and float used to be looked for from each digit of a run of digits, each time scanning to the end
    // of the run, and a symbol was matched over the whole rest of a run after each number that wins against it at the same
    // position (7 seconds for 100,000 digits, 2 seconds for 100,000 characters of `1-`).
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("1-")]
    [InlineData("+1")]
    public async Task Highlight_ClojureLongNumberRun_CompletesInReasonableTime(string part)
    {
        var code = "(f " + string.Concat(Enumerable.Repeat(part, 300_000 / part.Length)) + ")\n(def x 1)";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "clojure", out isFallback), $"Highlighting {code.Length} characters of 'clojure'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'clojure' was abandoned.");
        Assert.EndsWith("(<span class=\"hljs-keyword\">def</span> <span class=\"hljs-title\">x</span> <span class=\"hljs-number\">1</span>)", html, StringComparison.Ordinal);
    }

    // The PostgreSQL illegal pattern `\W\s*\(\*` used to be tried from each whitespace of a run of whitespace, each time
    // scanning to the end of the run (4 seconds for 100,000 characters, a minute for this document).
    [Theory]
    [InlineData("\n")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" \n")]
    public async Task Highlight_PgsqlLongWhitespaceRun_CompletesInReasonableTime(string part)
    {
        var code = "SELECT 1;" + string.Concat(Enumerable.Repeat(part, 400_000 / part.Length)) + "SELECT 2;";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "pgsql", out isFallback), $"Highlighting {code.Length} characters of 'pgsql'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'pgsql' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-keyword\">SELECT</span> <span class=\"hljs-number\">2</span>;", html, StringComparison.Ordinal);
    }

    // A urlencoded value used to be matched over the whole rest of a run after each `=` or name that wins against it at the
    // same position, and a name was looked for from each character of a run of name characters (4 seconds for 100,000
    // characters, a minute for this document).
    [Theory]
    [InlineData("=")]
    [InlineData("a=")]
    [InlineData("=a")]
    [InlineData("a==")]
    public async Task Highlight_UrlEncodedLongRun_CompletesInReasonableTime(string part)
    {
        var code = "a=1&" + string.Concat(Enumerable.Repeat(part, 400_000 / part.Length)) + "&b=2";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "urlencoded", out isFallback), $"Highlighting {code.Length} characters of 'urlencoded'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'urlencoded' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-attr\">b</span><span class=\"hljs-punctuation\">=</span><span class=\"hljs-string\">2</span>", html, StringComparison.Ordinal);
    }

    // A YAML plain scalar used to be matched over the whole rest of a run after each quoted string, number, literal or tag
    // that wins against it at the same position (2 to 3 seconds for 100,000 characters, a minute for this document).
    [Theory]
    [InlineData("\"", false)]
    [InlineData("\"a\"", false)]
    [InlineData("\"a\"'a'", false)]
    [InlineData("1-", false)]
    [InlineData("true\"a\"", false)]
    [InlineData("!x\"a\"", false)]
    [InlineData("\"", true)]
    [InlineData("1-", true)]
    public async Task Highlight_YamlLongRun_CompletesInReasonableTime(string part, bool inFlowCollection)
    {
        var run = string.Concat(Enumerable.Repeat(part, 400_000 / part.Length));
        var code = "a: " + (inFlowCollection ? "[" + run + "]" : run) + "\nkey: 1";

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, "yaml", out isFallback), $"Highlighting {code.Length} characters of 'yaml'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'yaml' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-attr\">key:</span> <span class=\"hljs-number\">1</span>", html, StringComparison.Ordinal);
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

    // The guard against grammars that stop making progress used to count every hit at the same index, including the ends
    // of ten thousand nested modes closing together (Apache), and allowed only three hits per character although MATLAB
    // makes three zero-width hits for each `)`: both documents were abandoned (plain text).
    [Theory]
    [InlineData("apache")]
    [InlineData("matlab")]
    public void Highlight_ManyHitsThatMakeProgress_IsNotAbandoned(string language)
    {
        var (code, end) = language switch
        {
            "apache" => ("Foo " + string.Concat(Enumerable.Repeat("%{", 20_000)) + "\nListen 80", "<span class=\"hljs-attribute\">Listen</span> <span class=\"hljs-number\">80</span>"),
            "matlab" => (new string(')', 100_000) + "\nx = 1", "x = <span class=\"hljs-number\">1</span>"),
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };

        var html = HighlightWithFallbackDetection(code, language, out var isFallback);

        Assert.False(isFallback, $"Highlighting {code.Length} characters of '{language}' was abandoned.");
        Assert.EndsWith(end, html, StringComparison.Ordinal);
    }

    // Each fragment of these documents leaves the embedded language one or two modes deeper, and resuming a fragment used
    // to reopen the scope of every one of them: the output grew with the square of the input (hundreds of megabytes, then
    // an OverflowException, for the longest ones).
    [Theory]
    [InlineData("erb")]
    [InlineData("node-repl")]
    [InlineData("python-repl")]
    [InlineData("clojure-repl")]
    public async Task Highlight_FragmentsThatNestDeeper_OutputIsLinear(string language)
    {
        var code = language switch
        {
            "erb" => string.Concat(Enumerable.Repeat("<% \"#{ %>", 20_000)),
            "node-repl" => "> `\n" + string.Concat(Enumerable.Repeat("... ${`\n", 20_000)),
            "python-repl" => ">>> f\"{\n" + string.Concat(Enumerable.Repeat("... f\"{\n", 20_000)),
            "clojure-repl" => "user=> (\n" + string.Concat(Enumerable.Repeat("  #_=> (a\n", 20_000)),
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };

        var isFallback = false;
        var html = await HighlightWithinBudget(() => HighlightWithFallbackDetection(code, language, out isFallback), $"Highlighting {code.Length} characters of '{language}'");

        Assert.False(isFallback, $"Highlighting {code.Length} characters of '{language}' was abandoned.");
        Assert.HasCountLessThan(code.Length * 100, html);
    }

    private static async Task<string> HighlightWithinBudget(Func<string> highlight, string description, TimeSpan? budget = null)
    {
        budget ??= Budget;
        var cpuTime = TimeSpan.Zero;
        var task = Task.Run(() =>
        {
            var stopwatch = ThreadCpuStopwatch.StartNew();
            try
            {
                return highlight();
            }
            finally
            {
                cpuTime = stopwatch.Elapsed;
            }
        });

        var finished = await Task.WhenAny(task, Task.Delay(HangTimeout)) == task;
        Assert.True(finished, $"{description} did not finish within {HangTimeout.TotalMinutes:F0} minutes.");

        var html = await task;
        Assert.True(cpuTime < budget, $"{description} took {cpuTime.TotalSeconds:F1}s of CPU time, more than {budget.Value.TotalSeconds:F0}s.");
        return html;
    }
}
