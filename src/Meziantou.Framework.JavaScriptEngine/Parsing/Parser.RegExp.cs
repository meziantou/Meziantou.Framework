using System.Collections.Concurrent;
using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Regex;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    // The result of the validation of the patterns (null: valid), shared by the parsers: the same scripts are parsed on every page load
    private static readonly ConcurrentDictionary<(string Pattern, string Flags), string?> RegExpValidationCache = new();

    private RegExpLiteral ParseRegularExpressionLiteral()
    {
        var start = _token.Start;
        _token = _lexer.RescanRegularExpression(_token);
        var pattern = _token.Value!;
        var flags = _token.Raw!;
        var error = ValidateRegularExpression(pattern, flags);
        if (error is not null)
            Raise(start, "Invalid regular expression: /" + pattern + "/" + flags + ": " + error);

        Next();
        return Finish(new RegExpLiteral { Pattern = pattern, Flags = flags }, start);
    }

    /// <summary>Validates the flags and the pattern of a regular expression (the early errors of RegularExpressionLiteral); returns the error message, or null.</summary>
    internal static string? ValidateRegularExpression(string pattern, string flags)
    {
        var options = RegexPatternOptions.None;
        foreach (var c in flags)
        {
            var option = c switch
            {
                'd' => RegexPatternOptions.HasIndices,
                'g' => RegexPatternOptions.Global,
                'i' => RegexPatternOptions.IgnoreCase,
                'm' => RegexPatternOptions.Multiline,
                's' => RegexPatternOptions.DotAll,
                'u' => RegexPatternOptions.Unicode,
                'v' => RegexPatternOptions.UnicodeSets,
                'y' => RegexPatternOptions.Sticky,
                _ => RegexPatternOptions.None,
            };

            if (option == RegexPatternOptions.None || (options & option) != 0)
                return "Invalid flags";

            options |= option;
        }

        if ((options & RegexPatternOptions.Unicode) != 0 && (options & RegexPatternOptions.UnicodeSets) != 0)
            return "Invalid flags";

        if ((options & RegexPatternOptions.UnicodeSets) != 0)
        {
            // The v flag is the u flag (Unicode mode) plus the class set grammar
            options |= RegexPatternOptions.Unicode;
        }

        var key = (pattern, flags);
        if (RegExpValidationCache.TryGetValue(key, out var cached))
            return cached;

        string? result = null;
        var tree = RegexSyntaxTree.ParseText(pattern, new RegexParseOptions(RegexDialect.JavaScript) { PatternOptions = options });
        foreach (var diagnostic in tree.GetDiagnostics())
        {
            // REGEX0200: the pattern is nested too deeply to be checked on the stack of the parser (the rest is not
            // validated here: the RegExp constructor reports the error at runtime)
            if (diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "REGEX0200")
            {
                result = diagnostic.Message;
                break;
            }
        }

        if (RegExpValidationCache.Count > 10_000)
        {
            RegExpValidationCache.Clear();
        }

        RegExpValidationCache.TryAdd(key, result);
        return result;
    }
}
