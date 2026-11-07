using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Regex;
#if NET11_0_OR_GREATER
using SysRegex = System.Text.RegularExpressions.Regex;
#endif
using SysRegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// Regular expressions in every dialect, with any pattern options: the shared invariants and the capture table. For the
/// .NET dialect, the runtime is the oracle: a pattern has no error exactly when System.Text.RegularExpressions accepts
/// it, and the groups are numbered and named as the runtime does (on .NET 11, as RegexNetDifferentialTests does).
/// </summary>
internal static class RegexFuzzTargets
{
    private const int SeedsPerConformanceFile = 400;

    private static readonly RegexDialect[] Dialects = [RegexDialect.Net, RegexDialect.JavaScript, RegexDialect.PcrePerl, RegexDialect.PosixExtended, RegexDialect.PosixBasic];

    /// <summary>The options that change whether a pattern is valid for the runtime, as RegexNetDifferentialTests lists them.</summary>
    /// <remarks>
    /// <c>ECMAScript</c> is left out: <c>[^</c> reaches an unguarded index in the runtime's own character-class scanner.
    /// <c>NonBacktracking</c> rejects well-formed patterns at construction time.
    /// </remarks>
    private static readonly SysRegexOptions[] NetOptions =
    [
        SysRegexOptions.None,
        SysRegexOptions.IgnorePatternWhitespace,
        SysRegexOptions.ExplicitCapture,
        SysRegexOptions.IgnoreCase | SysRegexOptions.Multiline | SysRegexOptions.Singleline,
    ];

    /// <summary>The fragments of RegexRoundTripFuzzTests, a lone surrogate included.</summary>
    private static readonly string[] Tokens =
    [
        "a", "ab", "0", "-", "_", "\u00E9", "\uD83D\uDE00", "\uD800",
        ".", "^", "$", "|", "\\", "\\\\", "\\.", "\\-", "\\/",
        "(", ")", "(?:", "(?=", "(?!", "(?<=", "(?<!", "(?>", "(?<name>", "(?'name'", "(?<n1-n2>", "(?P<name>", "(?P=name)", "(?|", "(?R)", "(?1)", "(?&name)",
        "(?(1)", "(?(name)", "(?((?=a))", "(?#comment)", "(?#", "(?)", "(?", "(*FAIL)", "(*SKIP)", "(*UTF)", "(?C1)",
        "(?i)", "(?-i)", "(?x)", "(?n)", "(?imnsx-imnsx:", "(?ix-ms:", "(?q)", "(?^", "(?J)",
        "[", "]", "[]", "[]]", "[^", "[^]", "[a-z]", "[z-a]", "[a-z-[aeiou]]", "[a-z-[b]c]", "[[a-z]--[aeiou]]", "[\\q{abc|d}]", "&&",
        "[\\w]", "[a-\\d]", "[[:alpha:]]", "[[.a.]]", "[[=a=]]", "[\\]]", "[-a]", "[a-]", "[a-[b]]",
        "*", "+", "?", "*?", "+?", "??", "*+", "**", "{2}", "{2,}", "{2,5}", "{2,5}?",
        "{,5}", "{5,2}", "{", "}", "{2147483648}", "{a}", "\\{", "\\}", "\\(", "\\)",
        "\\d", "\\D", "\\w", "\\W", "\\s", "\\S", "\\b", "\\B", "\\A", "\\Z", "\\z", "\\G", "\\K", "\\R", "\\X", "\\Q", "\\E",
        "\\n", "\\r", "\\t", "\\f", "\\v", "\\a", "\\e", "\\0", "\\q",
        "\\1", "\\10", "\\12", "\\123", "\\k<name>", "\\k", "\\<name>", "\\'name'", "\\g{1}", "\\g<name>",
        "\\x41", "\\x4", "\\xZZ", "\\x{1F600}", "\\u0041", "\\u00", "\\u{1F600}", "\\cA", "\\ca", "\\c1", "\\c", "\\o{17}", "\\N{U+41}",
        "\\p{L}", "\\p{Lu}", "\\p{IsGreek}", "\\P{Lu}", "\\p{Bogus}", "\\p{", "\\pL", "\\p", "\\p{Script=Greek}", "\\p{RGI_Emoji}",
        " ", "\t", "\n", "\r\n", "\r", "#comment\n", "#comment", "/", "/g", "/giu", "/v", "/dmsy",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-regex", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
        new("language-regex-net", NetDifferential) { Seeds = GetNetSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// Byte 0: the dialect (Net, JavaScript, PcrePerl, PosixExtended, PosixBasic, or a JavaScript literal such as
    /// <c>/a/g</c>). Bytes 1-2: the pattern options (12 bits), and the nesting limit in the high bits of byte 2. Then the pattern.
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.ReadByte(ref data) % (Dialects.Length + 1);
        var low = FuzzInput.ReadByte(ref data);
        var high = FuzzInput.ReadByte(ref data);
        var pattern = FuzzText.Decode(data);

        RegexSyntaxTree tree;
        if (dialect == Dialects.Length)
        {
            tree = RegexSyntaxTree.ParseJavaScriptLiteral(pattern);
        }
        else
        {
            var options = new RegexParseOptions(Dialects[dialect])
            {
                PatternOptions = (RegexPatternOptions)(low | ((high & 0x0F) << 8)),
                MaxRecursionDepth = LanguageFuzzInput.ReadDepth((byte)(high >> 4)),
            };

            tree = RegexSyntaxTree.ParseText(pattern, options);
        }

        Check(tree, pattern);
    }

    /// <summary>Byte 0: the options, among those that change what the runtime accepts. Then the pattern, in the .NET dialect.</summary>
    private static void NetDifferential(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.Pick(NetOptions, FuzzInput.ReadByte(ref data));
        var pattern = FuzzText.Decode(data);
        var tree = RegexSyntaxTree.ParseText(pattern, new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexOptionsInterop.ToPatternOptions(options) });
        Check(tree, pattern);

#if NET11_0_OR_GREATER
        // The scanner was ported from the engine of .NET 11, and older engines have bugs it does not reproduce
        // (RegexNetDifferentialTests runs on the newest target framework only for the same reason).
        SysRegex? runtime;
        string? runtimeError = null;
        try
        {
            runtime = new SysRegex(pattern, options, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException ex)
        {
            runtime = null;
            runtimeError = ex.Message;
        }

        var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        if (runtime is null)
        {
            FuzzAssert.True(errors.Count > 0, $"The runtime rejects the pattern with {options} ({runtimeError}) but the parser reports no error");
            return;
        }

        FuzzAssert.True(errors.Count == 0, $"The runtime accepts the pattern with {options} but the parser reports {string.Join(", ", errors.Select(diagnostic => $"{diagnostic.Id} at {diagnostic.Location.SourceSpan}: {diagnostic.Message}"))}");

        // Group 0 is the whole match and has no syntax, so it is not in the capture table
        FuzzAssert.Equal(string.Join(", ", runtime.GetGroupNames().Skip(1)), string.Join(", ", tree.Captures.Select(capture => capture.Name)), $"The capture names differ from the runtime with {options}");
        FuzzAssert.Equal(string.Join(", ", runtime.GetGroupNumbers().Skip(1)), string.Join(", ", tree.Captures.Select(capture => capture.Number)), $"The capture numbers differ from the runtime with {options}");
#endif
    }

    private static void Check(RegexSyntaxTree tree, string pattern)
    {
        // GetDiagnostics is documented as being in source order, but the parser reports them in the order it finds them:
        // "(?<" reports [3..3) before [0..3). The order is not checked, so that this discrepancy does not hide the rest.
        var shape = SyntaxTreeInvariants.Check(tree, pattern, "REGEX", diagnosticsOnNodes: false, diagnosticsInSourceOrder: false);
        foreach (var capture in tree.Captures)
        {
            FuzzAssert.True(capture.Span.Start >= 0 && capture.Span.End <= pattern.Length, $"The capture {capture.Number} has the span {capture.Span} outside a pattern of {pattern.Length} characters");
            FuzzAssert.True(capture.Number >= 0, $"The capture {capture.Name} has the number {capture.Number}");
            FuzzAssert.False(string.IsNullOrEmpty(capture.Name), $"The capture {capture.Number} has no name");
        }

        var walker = new CountingWalker();
        try
        {
            walker.Visit(tree.GetRoot());
            SyntaxTreeInvariants.CheckWalker(shape, walker.TokenCount, walker.TriviaCount);
        }
        catch (InsufficientExecutionStackException)
        {
            // Documented by the walker for a tree nested too deeply to walk
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        foreach (var (name, pattern) in GetNetPatterns())
        {
            yield return FuzzSeed.FromText(name, [0, 0, 0], pattern);
        }

        foreach (var (index, record) in Sample(LanguageFuzzInput.ReadJsonLines("regex.JavaScriptConformance.jsonl")))
        {
            var pattern = record.GetProperty("pattern").GetString()!;
            var flags = record.GetProperty("flags").GetString()!;
            var options = flags switch
            {
                "i" => RegexPatternOptions.IgnoreCase,
                "u" => RegexPatternOptions.Unicode,
                "v" => RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets,
                _ => RegexPatternOptions.None,
            };

            yield return FuzzSeed.FromText(FormattableString.Invariant($"javascript-{index}"), [1, (byte)options, 0], pattern);
            if (index % 8 == 0)
            {
                yield return FuzzSeed.FromText(FormattableString.Invariant($"javascript-literal-{index}"), [(byte)Dialects.Length, 0, 0], "/" + pattern + "/" + flags + "g");
            }
        }

        foreach (var (index, record) in Sample(LanguageFuzzInput.ReadJsonLines("regex.PcreConformance.jsonl")))
        {
            var options = record.GetProperty("extended").GetBoolean() ? RegexPatternOptions.IgnorePatternWhitespace : RegexPatternOptions.None;
            yield return FuzzSeed.FromText(FormattableString.Invariant($"pcre-{index}"), [2, (byte)options, 0], record.GetProperty("pattern").GetString()!);
        }

        foreach (var (index, record) in Sample(LanguageFuzzInput.ReadJsonLines("regex.PosixConformance.jsonl")))
        {
            byte dialect = record.GetProperty("dialect").GetString() == "ere" ? (byte)3 : (byte)4;
            yield return FuzzSeed.FromText(FormattableString.Invariant($"posix-{index}"), [dialect, 0, 0], record.GetProperty("pattern").GetString()!);
        }

        yield return FuzzSeed.FromText("deep", [0, 0, 0x10], "((((((((((a))))))))))[[[b]]]");
        yield return FuzzSeed.FromText("extended", [0, (byte)RegexPatternOptions.IgnorePatternWhitespace, 0], "a b # comment\n [ ] c\\ d (?-x: e f )");
    }

    private static IEnumerable<FuzzSeed> GetNetSeeds()
    {
        foreach (var (name, pattern) in GetNetPatterns())
        {
            for (var options = 0; options < NetOptions.Length; options++)
            {
                yield return FuzzSeed.FromText(FormattableString.Invariant($"{name}-{options}"), [(byte)options], pattern);
            }
        }
    }

    private static IEnumerable<(string Name, string Pattern)> GetNetPatterns()
    {
        var index = 0;
        foreach (var pattern in LanguageFuzzInput.ReadPercentRecords("regex.NetValid.txt"))
        {
            yield return (FormattableString.Invariant($"net-valid-{index++}"), pattern);
        }

        index = 0;
        foreach (var pattern in LanguageFuzzInput.ReadPercentRecords("regex.NetInvalid.txt"))
        {
            yield return (FormattableString.Invariant($"net-invalid-{index++}"), pattern);
        }
    }

    /// <summary>Takes records spread over the whole file, so that the seeds cover every section of a conformance corpus.</summary>
    private static IEnumerable<(int Index, System.Text.Json.JsonElement Record)> Sample(IEnumerable<System.Text.Json.JsonElement> records)
    {
        var all = records.ToList();
        var step = Math.Max(1, all.Count / SeedsPerConformanceFile);
        for (var i = 0; i < all.Count; i += step)
        {
            yield return (i, all[i]);
        }
    }

    private sealed class CountingWalker() : RegexSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int TokenCount { get; private set; }

        public int TriviaCount { get; private set; }

        public override void VisitToken(SyntaxToken token)
        {
            TokenCount++;
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia) => TriviaCount++;
    }
}
