namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Parses a pattern the way PCRE and Perl do.</summary>
/// <remarks>
/// PCRE is the Perl grammar with more in it than .NET has -- possessive quantifiers, atomic groups reachable through
/// the same header, POSIX bracket expressions, and <c>\Q…\E</c> -- and without .NET's character-class subtraction and
/// balancing groups. All of that is expressed as flavor features, so the shared parser needs no PCRE-specific code.
/// </remarks>
internal sealed class PcreRegexParser : PerlStyleRegexParser
{
    public PcreRegexParser(string text, RegexParseOptions parseOptions)
        : base(text, parseOptions)
    {
    }
}
