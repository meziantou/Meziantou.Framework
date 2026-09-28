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

    // A Svelte rune used to be rejected by a lookbehind that scanned back to the start of its line for a `//`, which is
    // quadratic on a long line of runes (about 10 seconds for this document).
    [Fact]
    public async Task Highlight_SvelteLongLineOfRunes_CompletesInReasonableTime()
    {
        var code = "<script>\n" + string.Concat(Enumerable.Repeat("$state(0);", 20_000));

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "svelte", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'svelte' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-built_in\">$state</span><span class=\"language-javascript\">(<span class=\"hljs-number\">0</span>);</span>", await highlight, StringComparison.Ordinal);
    }

    // An unclosed dynamic argument of a Vue directive (`:[key`) used to be scanned to the end of the attribute name from
    // each `:[` of the name (about 8 seconds for this document).
    [Fact]
    public async Task Highlight_VueLongRunOfUnclosedDynamicArguments_CompletesInReasonableTime()
    {
        var code = "<div " + string.Concat(Enumerable.Repeat(":[", 100_000)) + ">\n<p>";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "vue", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'vue' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("&gt;</span>\n<span class=\"hljs-tag\">&lt;<span class=\"hljs-name\">p</span>&gt;</span>", await highlight, StringComparison.Ordinal);
    }

    // A Handlebars `[ abc ]` segment that is not closed used to be scanned to the end of the document from each `[` that
    // follows it, in the same mustache or in a later one (40, 20 and 25 seconds for these documents).
    [Theory]
    [InlineData("{{x ", "[a", "}}", 1_000_000)]
    [InlineData("{{x ", "a.[a", "}}", 1_000_000)]
    [InlineData("", "{{x [a}}\n", "", 2_000_000)]
    public async Task Highlight_HandlebarsUnclosedBracketSegments_CompletesInReasonableTime(string prefix, string part, string suffix, int length)
    {
        var code = prefix + string.Concat(Enumerable.Repeat(part, length / part.Length)) + suffix + "\n{{#if a}}";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "handlebars", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'handlebars' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("{{#<span class=\"hljs-name\"><span class=\"hljs-built_in\">if</span></span> a}}</span>", await highlight, StringComparison.Ordinal);
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

    // The end of a Haml attribute hash (`}` after optional whitespace, or the end of a line that does not end with a
    // comma) used to be looked for by scanning a run of whitespace from each of its positions (30 seconds for this
    // document).
    [Fact]
    public async Task Highlight_HamlLongWhitespaceInAttributeHash_CompletesInReasonableTime()
    {
        var code = "%a{x" + new string(' ', 100_000) + "y\n%p text";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "haml", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'haml' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-tag\">%<span class=\"hljs-selector-tag\">p</span></span> text", await highlight, StringComparison.Ordinal);
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

    // A Nix path used to be matched from each `/` of a run of path pieces, which is quadratic on a long run that is not
    // followed by a whitespace or a `;` (half a minute to more than a minute for these documents).
    [Theory]
    [InlineData("a/")]
    [InlineData("./")]
    public async Task Highlight_NixLongPathRun_CompletesInReasonableTime(string piece)
    {
        var code = string.Concat(Enumerable.Repeat(piece, 40_000)) + ")\nx = ./y;";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "nix", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'nix' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-symbol\">./y</span>;", await highlight, StringComparison.Ordinal);
    }

    // An AWK `${…}` variable used to be matched from each `${` of a line without `}` up to the end of the line (twenty
    // seconds for this document).
    [Fact]
    public async Task Highlight_AwkLongLineOfUnclosedVariables_CompletesInReasonableTime()
    {
        var code = string.Concat(Enumerable.Repeat("${", 40_000)) + "\nBEGIN";

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "awk", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'awk' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-keyword\">BEGIN</span>", await highlight, StringComparison.Ordinal);
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

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "toml", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'toml' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-attr\">key</span> = <span class=\"hljs-number\">1</span>", await highlight, StringComparison.Ordinal);
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

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "mermaid", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'mermaid' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("A <span class=\"hljs-operator\">--&gt;</span> B", await highlight, StringComparison.Ordinal);
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

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "mermaid", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'mermaid' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("A <span class=\"hljs-operator\">--&gt;</span> B", await highlight, StringComparison.Ordinal);
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

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "asciidoc", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'asciidoc' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-strong\">*end*</span>", await highlight, StringComparison.Ordinal);
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

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "asciidoc", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'asciidoc' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.EndsWith("<span class=\"hljs-link\">https://example.com</span>[<span class=\"hljs-string\">end</span>]", await highlight, StringComparison.Ordinal);
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
        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "haskell", out isFallback));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'haskell' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'haskell' was abandoned.");
        Assert.EndsWith("<span class=\"hljs-title\">main</span> = <span class=\"hljs-number\">1</span>", await highlight, StringComparison.Ordinal);
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
        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "clojure", out isFallback));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'clojure' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.False(isFallback, $"Highlighting {code.Length} characters of 'clojure' was abandoned.");
        Assert.EndsWith("(<span class=\"hljs-keyword\">def</span> <span class=\"hljs-title\">x</span> <span class=\"hljs-number\">1</span>)", await highlight, StringComparison.Ordinal);
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
        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, language, out isFallback));
        var finished = await Task.WhenAny(highlight, Task.Delay(Budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of '{language}' did not finish within {Budget.TotalSeconds:F0}s.");
        Assert.False(isFallback, $"Highlighting {code.Length} characters of '{language}' was abandoned.");
        Assert.HasCountLessThan(code.Length * 100, await highlight);
    }
}
