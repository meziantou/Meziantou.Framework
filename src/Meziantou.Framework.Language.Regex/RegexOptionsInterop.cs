using SysRegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Converts between <see cref="RegexPatternOptions"/> and the .NET engine's own options.</summary>
/// <remarks>
/// Only the options that change how a pattern is <em>read</em> are represented. <c>Compiled</c>, <c>RightToLeft</c>,
/// <c>CultureInvariant</c>, and <c>NonBacktracking</c> affect matching, which this library does not do, so they are
/// dropped in both directions.
/// </remarks>
public static class RegexOptionsInterop
{
    /// <summary>Maps the .NET options that affect parsing onto flavor-neutral options.</summary>
    public static RegexPatternOptions ToPatternOptions(SysRegexOptions options)
    {
        var result = RegexPatternOptions.None;
        if (options.HasFlag(SysRegexOptions.IgnoreCase))
        {
            result |= RegexPatternOptions.IgnoreCase;
        }

        if (options.HasFlag(SysRegexOptions.Multiline))
        {
            result |= RegexPatternOptions.Multiline;
        }

        if (options.HasFlag(SysRegexOptions.Singleline))
        {
            result |= RegexPatternOptions.Singleline;
        }

        if (options.HasFlag(SysRegexOptions.ExplicitCapture))
        {
            result |= RegexPatternOptions.ExplicitCapture;
        }

        if (options.HasFlag(SysRegexOptions.IgnorePatternWhitespace))
        {
            result |= RegexPatternOptions.IgnorePatternWhitespace;
        }

        if (options.HasFlag(SysRegexOptions.ECMAScript))
        {
            result |= RegexPatternOptions.EcmaScript;
        }

        return result;
    }

    /// <summary>Maps flavor-neutral options back onto .NET options. Options with no .NET equivalent are dropped.</summary>
    public static SysRegexOptions ToRegexOptions(RegexPatternOptions options)
    {
        var result = SysRegexOptions.None;
        if ((options & RegexPatternOptions.IgnoreCase) != RegexPatternOptions.None)
        {
            result |= SysRegexOptions.IgnoreCase;
        }

        if ((options & RegexPatternOptions.Multiline) != RegexPatternOptions.None)
        {
            result |= SysRegexOptions.Multiline;
        }

        if ((options & (RegexPatternOptions.Singleline | RegexPatternOptions.DotAll)) != RegexPatternOptions.None)
        {
            result |= SysRegexOptions.Singleline;
        }

        if ((options & RegexPatternOptions.ExplicitCapture) != RegexPatternOptions.None)
        {
            result |= SysRegexOptions.ExplicitCapture;
        }

        if ((options & RegexPatternOptions.IgnorePatternWhitespace) != RegexPatternOptions.None)
        {
            result |= SysRegexOptions.IgnorePatternWhitespace;
        }

        if ((options & RegexPatternOptions.EcmaScript) != RegexPatternOptions.None)
        {
            result |= SysRegexOptions.ECMAScript;
        }

        return result;
    }
}
