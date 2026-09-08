using System.Diagnostics;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Identifies a regular-expression dialect understood by <see cref="RegexSyntaxTree"/>.</summary>
/// <remarks>The set of dialects is closed. Use the static properties to obtain an instance.</remarks>
[DebuggerDisplay("{Name}")]
public sealed class RegexDialect
{
    /// <summary>What every dialect has, POSIX basic expressions included.</summary>
    private const RegexDialectFeatures CommonFeatures = RegexDialectFeatures.Backreferences;

    /// <summary>What every dialect except POSIX basic expressions has.</summary>
    private const RegexDialectFeatures ModernFeatures =
        CommonFeatures |
        RegexDialectFeatures.Alternation |
        RegexDialectFeatures.PlusAndQuestionQuantifiers;

    /// <summary>What the Perl-derived dialects share.</summary>
    private const RegexDialectFeatures PerlFeatures =
        ModernFeatures |
        RegexDialectFeatures.LazyQuantifiers |
        RegexDialectFeatures.ExtendedGroupSyntax |
        RegexDialectFeatures.NonCapturingGroups |
        RegexDialectFeatures.NamedGroups |
        RegexDialectFeatures.AngleNamedGroups |
        RegexDialectFeatures.Lookahead |
        RegexDialectFeatures.Lookbehind |
        RegexDialectFeatures.UnicodeCategories |
        RegexDialectFeatures.BareBraceIsLiteral;

    private RegexDialect(string name, RegexDialectFamily family, RegexDialectFeatures features)
    {
        Name = name;
        Family = family;
        Features = features;
    }

    /// <summary>The .NET dialect, as <c>System.Text.RegularExpressions</c> defines it.</summary>
    public static RegexDialect Net { get; } = new(
        "net",
        RegexDialectFamily.Net,
        PerlFeatures |
        RegexDialectFeatures.QuoteNamedGroups |
        RegexDialectFeatures.BalancingGroups |
        RegexDialectFeatures.AtomicGroups |
        RegexDialectFeatures.Conditionals |
        RegexDialectFeatures.InlineOptions |
        RegexDialectFeatures.IgnorePatternWhitespace |
        RegexDialectFeatures.CommentGroups |
        RegexDialectFeatures.CharacterClassSubtraction |
        RegexDialectFeatures.StrictEscapes |
        RegexDialectFeatures.AnchorsAZ);

    /// <summary>The ECMAScript dialect.</summary>
    /// <remarks>
    /// Both Unicode flags are honoured. <c>u</c> makes a surrogate pair one atom; <c>v</c> additionally turns on the
    /// class set grammar, so a class may nest, be intersected with <c>&amp;&amp;</c>, have another subtracted with
    /// <c>--</c>, and contain a <c>\q{…}</c> string disjunction.
    /// </remarks>
    public static RegexDialect JavaScript { get; } = new(
        "javascript",
        RegexDialectFamily.JavaScript,
        ModernFeatures |
        RegexDialectFeatures.LazyQuantifiers |
        RegexDialectFeatures.ExtendedGroupSyntax |
        RegexDialectFeatures.NonCapturingGroups |
        RegexDialectFeatures.NamedGroups |
        RegexDialectFeatures.AngleNamedGroups |
        RegexDialectFeatures.Lookahead |
        RegexDialectFeatures.Lookbehind |
        RegexDialectFeatures.UnicodeCategories |
        RegexDialectFeatures.UnicodeCategoriesRequireUnicodeFlag |
        RegexDialectFeatures.UnicodePropertyNames |
        RegexDialectFeatures.ClassSetOperations |
        RegexDialectFeatures.BareBraceIsLiteral);

    /// <summary>The PCRE and Perl dialect.</summary>
    public static RegexDialect PcrePerl { get; } = new(
        "pcre",
        RegexDialectFamily.Pcre,
        PerlFeatures |
        RegexDialectFeatures.QuoteNamedGroups |
        RegexDialectFeatures.PythonNamedGroups |
        RegexDialectFeatures.AtomicGroups |
        RegexDialectFeatures.PossessiveQuantifiers |
        RegexDialectFeatures.Conditionals |
        RegexDialectFeatures.BranchReset |
        RegexDialectFeatures.Recursion |
        RegexDialectFeatures.BacktrackingVerbs |
        RegexDialectFeatures.InlineOptions |
        RegexDialectFeatures.IgnorePatternWhitespace |
        RegexDialectFeatures.CommentGroups |
        RegexDialectFeatures.PosixBracketExpressions |
        RegexDialectFeatures.QuotedLiterals |
        RegexDialectFeatures.UnicodePropertyNames |
        RegexDialectFeatures.StrictEscapes |
        RegexDialectFeatures.AnchorsAZ |
        RegexDialectFeatures.KeepOut);

    /// <summary>POSIX extended regular expressions (ERE).</summary>
    public static RegexDialect PosixExtended { get; } = new(
        "ere",
        RegexDialectFamily.Posix,
        ModernFeatures | RegexDialectFeatures.PosixBracketExpressions);

    /// <summary>POSIX basic regular expressions (BRE), in which the delimiters are spelled with a backslash.</summary>
    /// <remarks>
    /// <c>\(…\)</c> groups, <c>\{n,m\}</c> bounds, and backreferences are all supported. Alternation with <c>\|</c>
    /// and the <c>\+</c> and <c>\?</c> quantifiers are GNU extensions rather than POSIX proper, and are accepted.
    /// </remarks>
    public static RegexDialect PosixBasic { get; } = new(
        "bre",
        RegexDialectFamily.Posix,
        CommonFeatures |
        RegexDialectFeatures.PosixBracketExpressions |
        RegexDialectFeatures.EscapedGroupDelimiters |
        RegexDialectFeatures.Alternation |
        RegexDialectFeatures.PlusAndQuestionQuantifiers);

    /// <summary>The canonical lowercase name of the dialect, such as <c>net</c> or <c>pcre</c>.</summary>
    public string Name { get; }

    /// <summary>The grammar family the dialect belongs to.</summary>
    public RegexDialectFamily Family { get; }

    /// <summary>The optional constructs the dialect supports.</summary>
    public RegexDialectFeatures Features { get; }

    /// <summary>Returns <see langword="true"/> when the dialect supports every feature in <paramref name="feature"/>.</summary>
    public bool HasFeature(RegexDialectFeatures feature) => (Features & feature) == feature;

    /// <summary>Resolves a dialect from its name. Recognizes the canonical names and common aliases such as <c>dotnet</c>.</summary>
    public static bool TryParse(string? name, [NotNullWhen(true)] out RegexDialect? dialect)
    {
        dialect = name?.Trim().ToLowerInvariant() switch
        {
            "net" or "dotnet" or ".net" or "csharp" => Net,
            "javascript" or "js" or "ecmascript" or "es" => JavaScript,
            "pcre" or "pcre2" or "perl" => PcrePerl,
            "ere" or "posix" or "posix-extended" or "egrep" => PosixExtended,
            "bre" or "posix-basic" or "grep" => PosixBasic,
            _ => null,
        };

        return dialect is not null;
    }

    public override string ToString() => Name;
}
