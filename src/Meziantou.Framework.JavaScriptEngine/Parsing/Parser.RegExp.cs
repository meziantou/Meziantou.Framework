using System.Collections.Concurrent;
using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    // The result of the validation of the patterns (null: valid), shared by the parsers: the same scripts are parsed on every page load
    private static readonly ConcurrentDictionary<(string Pattern, string Flags), string?> RegExpValidationCache = new();
    private static int s_validationCacheCount;

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
        if (!RegExpFlagsExtensions.TryParse(flags, out var parsedFlags))
            return "Invalid flags";

        var key = (pattern, flags);
        if (RegExpValidationCache.TryGetValue(key, out var cached))
            return cached;

        // A pattern nested too deeply to be parsed on the stack is not validated here: the RegExp constructor reports
        // the error at runtime
        string? result = null;
        if (!RegExpParser.TryParse(pattern, parsedFlags, out _, out var error) && error is not RegExpParser.TooLargeMessage)
        {
            result = error;
        }

        // ConcurrentDictionary.Count takes all the locks of the dictionary: the number of entries is tracked separately
        if (Interlocked.Increment(ref s_validationCacheCount) > 10_000)
        {
            Interlocked.Exchange(ref s_validationCacheCount, 0);
            RegExpValidationCache.Clear();
        }

        RegExpValidationCache.TryAdd(key, result);
        return result;
    }
}
