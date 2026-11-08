using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Regex;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// The pattern parser of the engine accepts or rejects a pattern without any other exception, and agrees with the
/// JavaScript dialect of <c>Meziantou.Framework.Language.Regex</c>, an independent implementation of the same grammar:
/// the same patterns are valid, with the same number of capturing groups and the same group names. A valid pattern
/// compiles to a program.
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    private const int RegExpParserMaxNestingDepth = 256;

    private static readonly string[] RegExpParserFlags = ["", "i", "m", "s", "u", "v", "iu", "iv", "ims", "imsu", "imsv"];

    // The input is the flags (an index into RegExpParserFlags) and the pattern
    private static void RegExpParser(ReadOnlySpan<byte> data)
    {
        var flagsText = FuzzInput.Pick(RegExpParserFlags, FuzzInput.ReadByte(ref data));
        var pattern = FuzzText.Decode(data);
        FuzzAssert.True(RegExpFlagsExtensions.TryParse(flagsText, out var flags), $"The flags '{flagsText}' are not valid");

        var isValid = JavaScriptEngine.RegExpParser.TryParse(pattern, flags, out var result, out var error);
        var oracle = ParseWithLanguageRegex(pattern, flags);
        if (error is JavaScriptEngine.RegExpParser.TooLargeMessage || oracle.IsTooLarge)
            return;

        if (isValid)
        {
            FuzzAssert.True(result is not null, "TryParse returned true without a result");
            FuzzAssert.Equal(result.GroupCount + 1, result.GroupNames.Length, "The group names do not have an entry per group");
            FuzzAssert.True(RegExpProgram.Compile(pattern, flags, out var compileError) is not null, "A valid pattern does not compile: " + compileError);
        }
        else
        {
            FuzzAssert.True(!string.IsNullOrEmpty(error), "TryParse returned false without an error message");
        }

        // The two parsers read the Unicode properties and the identifier characters of the group names from different
        // versions of the Unicode data
        if (isValid != oracle.IsValid && !DependsOnUnicodeData(pattern, flags))
        {
            FuzzAssert.Fail($"The engine {(isValid ? "accepts" : "rejects (" + error + ")")} the pattern that Meziantou.Framework.Language.Regex {(oracle.IsValid ? "accepts" : "rejects (" + oracle.Error + ")")}");
        }

        if (isValid && oracle.IsValid)
        {
            FuzzAssert.Equal(oracle.GroupNames.Length - 1, result!.GroupCount, "The number of capturing groups differs from Meziantou.Framework.Language.Regex");
            for (var i = 1; i < oracle.GroupNames.Length; i++)
            {
                FuzzAssert.Equal(oracle.GroupNames[i], result.GroupNames[i], $"The name of the group {i} differs from Meziantou.Framework.Language.Regex");
            }
        }
    }

    private static (bool IsValid, bool IsTooLarge, string? Error, string?[] GroupNames) ParseWithLanguageRegex(string pattern, JavaScriptEngine.RegExpFlags flags)
    {
        var options = RegexPatternOptions.None;
        if (flags.HasFlag(JavaScriptEngine.RegExpFlags.IgnoreCase))
        {
            options |= RegexPatternOptions.IgnoreCase;
        }

        if (flags.HasFlag(JavaScriptEngine.RegExpFlags.Multiline))
        {
            options |= RegexPatternOptions.Multiline;
        }

        if (flags.HasFlag(JavaScriptEngine.RegExpFlags.DotAll))
        {
            options |= RegexPatternOptions.DotAll | RegexPatternOptions.Singleline;
        }

        if (flags.HasFlag(JavaScriptEngine.RegExpFlags.Unicode))
        {
            options |= RegexPatternOptions.Unicode;
        }

        if (flags.HasFlag(JavaScriptEngine.RegExpFlags.UnicodeSets))
        {
            options |= RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets;
        }

        var tree = RegexSyntaxTree.ParseText(pattern, new RegexParseOptions(RegexDialect.JavaScript) { PatternOptions = options, MaxRecursionDepth = RegExpParserMaxNestingDepth });
        foreach (var diagnostic in tree.GetDiagnostics())
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;

            // REGEX0200: the pattern is nested too deeply to be parsed on the stack
            return (false, diagnostic.Id == "REGEX0200", diagnostic.Message, []);
        }

        var groupNames = new string?[tree.Captures.Count + 1];
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is RegexNamedGroupSyntax named && named.Number < groupNames.Length)
            {
                groupNames[named.Number] = named.Name;
            }
        }

        return (true, false, null, groupNames);
    }

    /// <summary>
    /// Whether the pattern has a property escape (with the "u" or "v" flag), or a group name or a named backreference
    /// that has a non-ASCII character or an escape.
    /// </summary>
    private static bool DependsOnUnicodeData(string pattern, JavaScriptEngine.RegExpFlags flags)
    {
        if (flags.HasEitherUnicodeFlag() && (pattern.Contains("\\p{", StringComparison.Ordinal) || pattern.Contains("\\P{", StringComparison.Ordinal)))
            return true;

        for (var i = 0; i + 2 < pattern.Length; i++)
        {
            var isName = (pattern[i] == '(' && pattern[i + 1] == '?' && pattern[i + 2] == '<') || (pattern[i] == '\\' && pattern[i + 1] == 'k' && pattern[i + 2] == '<');
            if (!isName)
                continue;

            for (var j = i + 3; j < pattern.Length && pattern[j] != '>'; j++)
            {
                if (pattern[j] is > '\u007F' or '\\')
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<FuzzSeed> GetRegExpParserSeeds()
    {
        // The patterns and the flags of the conformance corpus of Meziantou.Framework.Language.Regex, checked against V8,
        // and of the V8 corpus of the matcher tests
        using (var stream = FuzzResources.Open(typeof(JavaScriptEngineFuzzTargets).Assembly, "JavaScriptConformance.jsonl"))
        using (var reader = new StreamReader(stream))
        {
            var index = 0;
            while (reader.ReadLine() is { Length: > 0 } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var pattern = root.GetProperty("pattern").GetString()!;
                var flags = root.GetProperty("flags").GetString()!;
                yield return FuzzSeed.FromText($"conformance-{index++}", [(byte)Math.Max(0, Array.IndexOf(RegExpParserFlags, flags))], pattern);
            }
        }

        foreach (var seed in GetRegExpSeeds())
        {
            // A RegExp seed is the pattern, NUL, the index of the flags in RegExpFlags, and the subject
            var bytes = seed.Data.Span;
            var pattern = FuzzInput.ReadUntil(ref bytes, Separator);
            var flags = FuzzInput.Pick(RegExpFlags, FuzzInput.ReadByte(ref bytes));
            var flagsIndex = Array.IndexOf(RegExpParserFlags, new string([.. flags.Where(c => c is 'i' or 'm' or 's' or 'u' or 'v').Order()]));
            yield return new FuzzSeed("parser-" + seed.Name, (byte[])[(byte)Math.Max(0, flagsIndex), .. pattern]);
        }
    }
}
