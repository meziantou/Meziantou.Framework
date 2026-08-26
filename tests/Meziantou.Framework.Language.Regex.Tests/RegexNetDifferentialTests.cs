using System.Text.RegularExpressions;
using SysRegex = System.Text.RegularExpressions.Regex;
using SysRegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Meziantou.Framework.Language.Regex.Tests;

#if NET11_0_OR_GREATER

/// <summary>
/// The .NET flavor exists to agree with the runtime, so the runtime is the oracle: a pattern is valid for us exactly
/// when <see cref="SysRegex"/> accepts it. A disagreement is a bug in the parser, never in the assertion.
/// </summary>
/// <remarks>
/// <para>
/// The equivalence is asserted in both directions, but only over validity. What error the runtime reports, and where,
/// is a stricter claim: it stops at the first problem while this parser reports every one it finds, and its offset is
/// where it gave up rather than where recovery put the span. Those are checked on hand-verified cases instead.
/// </para>
/// <para>
/// The comparison runs on the newest target framework only. The scanner was ported from the current engine, and the
/// engine changes between releases: .NET 10 rejects <c>(?(name)(?n))</c> and knows fewer Unicode block names than
/// .NET 11 does. Comparing against an older engine would assert that the parser reproduces bugs that engine has since
/// fixed. Every other test in this project runs on both target frameworks.
/// </para>
/// </remarks>
public sealed class RegexNetDifferentialTests
{
    /// <summary>
    /// The options that change whether a pattern is <em>valid</em>.
    /// </summary>
    /// <remarks>
    /// <c>Compiled</c>, <c>CultureInvariant</c>, and <c>RightToLeft</c> do not, so they are left out.
    /// <c>NonBacktracking</c> is left out because it rejects perfectly well-formed patterns at construction time,
    /// which would make the runtime a liar about what parses.
    /// </remarks>
    public static TheoryData<SysRegexOptions> ValidityAffectingOptions =>
    [
        SysRegexOptions.None,
        SysRegexOptions.IgnorePatternWhitespace,
        SysRegexOptions.ExplicitCapture,
        SysRegexOptions.IgnoreCase | SysRegexOptions.Multiline | SysRegexOptions.Singleline,
    ];

    [Theory]
    [MemberData(nameof(ValidityAffectingOptions))]
    public void CuratedPatterns_AgreeWithTheRuntime(SysRegexOptions options)
    {
        foreach (var pattern in RegexCorpus.Read("NetValid.txt").Concat(RegexCorpus.Read("NetInvalid.txt")))
        {
            AssertAgreesWithRuntime(pattern, options);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomFragmentSequences_AgreeWithTheRuntime(int seed)
    {
        // RegexOptions.None only. Extended mode and explicit capture are covered by the curated corpus, and ECMAScript
        // is kept out entirely: "[^" reaches an unguarded index in the runtime's own character-class scanner.
        foreach (var pattern in RegexRoundTripFuzzTests.GenerateFragmentSequences(seed, count: 400))
        {
            AssertAgreesWithRuntime(pattern, SysRegexOptions.None);
        }
    }

    [Theory]
    [InlineData(@"(a)\1", null)]
    [InlineData(@"\1(a)", null)]
    [InlineData(@"\1", RegexParseError.UndefinedNumberedReference)]
    // With fewer than ten groups the runtime falls back to the octal escape for a backspace, so this is not an error.
    [InlineData(@"\10", null)]
    [InlineData(@"\5(a)(b)", RegexParseError.UndefinedNumberedReference)]
    [InlineData(@"(?(foo)a|b)", null)]
    [InlineData(@"(?(1)a|b)", RegexParseError.AlternationHasUndefinedReference)]
    [InlineData(@"(?(1)a|b)(x)", null)]
    [InlineData(@"(?)", RegexParseError.QuantifierAfterNothing)]
    [InlineData(@"[]]", null)]
    [InlineData(@"[]", RegexParseError.UnterminatedBracket)]
    [InlineData(@"[a-z-[b]c]", RegexParseError.ExclusionGroupNotLast)]
    // "{2 , 3}" is not a well-formed bound, so the braces are ordinary characters.
    [InlineData(@"a{2 , 3}", null)]
    [InlineData(@"(?<0>x)", RegexParseError.CaptureGroupOfZero)]
    [InlineData(@"a**", RegexParseError.NestedQuantifiersNotParenthesized)]
    [InlineData(@"a*+", RegexParseError.NestedQuantifiersNotParenthesized)]
    [InlineData(@"*a", RegexParseError.QuantifierAfterNothing)]
    [InlineData(@"a{5,2}", RegexParseError.ReversedQuantifierRange)]
    [InlineData(@"[z-a]", RegexParseError.ReversedCharacterRange)]
    [InlineData(@"[a-\d]", RegexParseError.ShorthandClassInCharacterRange)]
    [InlineData(@"\q", RegexParseError.UnrecognizedEscape)]
    [InlineData(@"\x4", RegexParseError.InsufficientOrInvalidHexDigits)]
    [InlineData(@"\c", RegexParseError.MissingControlCharacter)]
    [InlineData(@"\c1", RegexParseError.UnrecognizedControlCharacter)]
    [InlineData(@"\p{Bogus}", RegexParseError.UnrecognizedUnicodeProperty)]
    // "\pL" is too short to reach the "{" check, so the engine calls it incomplete rather than malformed.
    [InlineData(@"\pL", RegexParseError.InvalidUnicodePropertyEscape)]
    [InlineData(@"\pLxy", RegexParseError.MalformedUnicodePropertyEscape)]
    [InlineData(@"\k<none>", RegexParseError.UndefinedNamedReference)]
    [InlineData(@"\k", RegexParseError.MalformedNamedReference)]
    [InlineData(@"(?<a-none>x)", RegexParseError.UndefinedNamedReference)]
    [InlineData(@"(a", RegexParseError.InsufficientClosingParentheses)]
    [InlineData(@"a)", RegexParseError.InsufficientOpeningParentheses)]
    [InlineData(@"(?#unterminated", RegexParseError.UnterminatedComment)]
    [InlineData(@"a\", RegexParseError.UnescapedEndingBackslash)]
    public void KnownEdgeCases_AgreeWithTheRuntime(string pattern, RegexParseError? expectedRuntimeError)
    {
        Assert.Equal(expectedRuntimeError, GetRuntimeError(pattern, SysRegexOptions.None));
        AssertAgreesWithRuntime(pattern, SysRegexOptions.None);
    }

    /// <summary>
    /// The group numbers and names the parser reports must be the ones the engine assigns, which the engine will say.
    /// </summary>
    [Fact]
    public void CaptureNumbering_MatchesTheRuntime()
    {
        foreach (var pattern in RegexCorpus.Read("NetValid.txt"))
        {
            var tree = RegexSyntaxTree.ParseText(pattern, RegexFlavor.Net);
            var runtime = new SysRegex(pattern, SysRegexOptions.None, TimeSpan.FromSeconds(5));

            // Group 0 is the whole match and has no syntax, so it is not in our table.
            Assert.Equal(runtime.GetGroupNames().Skip(1), tree.Captures.Select(capture => capture.Name), $"capture names of [{pattern}]");
            Assert.Equal(runtime.GetGroupNumbers().Skip(1), tree.Captures.Select(capture => capture.Number), $"capture numbers of [{pattern}]");
        }
    }

    /// <summary>
    /// A runtime that learns a new block name should fail here once rather than scatter failures across the fuzz.
    /// </summary>
    [Fact]
    public void UnicodePropertyNames_AreAllAcceptedByTheRuntime()
    {
        foreach (var name in Internals.NetUnicodeCategoryNames.All)
        {
            Assert.Null(GetRuntimeError($@"\p{{{name}}}", SysRegexOptions.None), $"the runtime rejected the block name '{name}'");
        }
    }

    private static void AssertAgreesWithRuntime(string pattern, SysRegexOptions options)
    {
        var parseOptions = new RegexParseOptions(RegexFlavor.Net) { PatternOptions = RegexOptionsInterop.ToPatternOptions(options) };
        var tree = RegexSyntaxTree.ParseText(pattern, parseOptions);

        RegexSyntaxAssert.TextIsFaithful(pattern, tree);

        var runtimeError = GetRuntimeError(pattern, options);
        var errors = tree.Diagnostics.Where(diagnostic => diagnostic.Severity == RegexDiagnosticSeverity.Error).ToArray();
        var reported = string.Join(", ", errors.Select(diagnostic => $"{diagnostic.Id} {diagnostic.Message}"));

        var reportedAnError = errors.Length > 0;
        if (runtimeError is null)
        {
            Assert.False(reportedAnError, $"The runtime accepted [{pattern}] with {options} but the parser reported {reported}.");
        }
        else
        {
            Assert.True(reportedAnError, $"The runtime rejected [{pattern}] with {options} as {runtimeError} but the parser reported nothing.");
        }
    }

    private static RegexParseError? GetRuntimeError(string pattern, SysRegexOptions options)
    {
        try
        {
            _ = new SysRegex(pattern, options, TimeSpan.FromSeconds(5));

            return null;
        }
        catch (RegexParseException exception)
        {
            return exception.Error;
        }
        catch (ArgumentException)
        {
            return RegexParseError.Unknown;
        }
        catch (IndexOutOfRangeException)
        {
            // "[^" under RegexOptions.ECMAScript reads past the end of the pattern in the runtime's own character-class
            // scanner. It is still a rejection, so it counts as one.
            return RegexParseError.Unknown;
        }
    }
}
#endif
