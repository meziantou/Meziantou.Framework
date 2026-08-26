using System.Diagnostics;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Identifies a regular-expression flavor understood by <see cref="RegexSyntaxTree"/>.</summary>
/// <remarks>The set of flavors is closed. Use the static properties to obtain an instance.</remarks>
[DebuggerDisplay("{Name}")]
public sealed class RegexFlavor
{
    /// <summary>What every flavor has, POSIX basic expressions included.</summary>
    private const RegexFlavorFeatures CommonFeatures = RegexFlavorFeatures.Backreferences;

    /// <summary>What every flavor except POSIX basic expressions has.</summary>
    private const RegexFlavorFeatures ModernFeatures =
        CommonFeatures |
        RegexFlavorFeatures.Alternation |
        RegexFlavorFeatures.PlusAndQuestionQuantifiers;

    /// <summary>What the Perl-derived flavors share.</summary>
    private const RegexFlavorFeatures PerlFeatures =
        ModernFeatures |
        RegexFlavorFeatures.LazyQuantifiers |
        RegexFlavorFeatures.ExtendedGroupSyntax |
        RegexFlavorFeatures.NonCapturingGroups |
        RegexFlavorFeatures.NamedGroups |
        RegexFlavorFeatures.AngleNamedGroups |
        RegexFlavorFeatures.Lookahead |
        RegexFlavorFeatures.Lookbehind |
        RegexFlavorFeatures.UnicodeCategories |
        RegexFlavorFeatures.BareBraceIsLiteral;

    private RegexFlavor(string name, RegexFlavorFamily family, RegexFlavorFeatures features)
    {
        Name = name;
        Family = family;
        Features = features;
    }

    /// <summary>The .NET flavor, as <c>System.Text.RegularExpressions</c> defines it.</summary>
    public static RegexFlavor Net { get; } = new(
        "net",
        RegexFlavorFamily.Net,
        PerlFeatures |
        RegexFlavorFeatures.QuoteNamedGroups |
        RegexFlavorFeatures.BalancingGroups |
        RegexFlavorFeatures.AtomicGroups |
        RegexFlavorFeatures.Conditionals |
        RegexFlavorFeatures.InlineOptions |
        RegexFlavorFeatures.IgnorePatternWhitespace |
        RegexFlavorFeatures.CommentGroups |
        RegexFlavorFeatures.CharacterClassSubtraction |
        RegexFlavorFeatures.StrictEscapes |
        RegexFlavorFeatures.AnchorsAZ);

    /// <summary>The ECMAScript flavor.</summary>
    /// <remarks>
    /// Both Unicode flags are honoured. <c>u</c> makes a surrogate pair one atom; <c>v</c> additionally turns on the
    /// class set grammar, so a class may nest, be intersected with <c>&amp;&amp;</c>, have another subtracted with
    /// <c>--</c>, and contain a <c>\q{…}</c> string disjunction.
    /// </remarks>
    public static RegexFlavor JavaScript { get; } = new(
        "javascript",
        RegexFlavorFamily.JavaScript,
        ModernFeatures |
        RegexFlavorFeatures.LazyQuantifiers |
        RegexFlavorFeatures.ExtendedGroupSyntax |
        RegexFlavorFeatures.NonCapturingGroups |
        RegexFlavorFeatures.NamedGroups |
        RegexFlavorFeatures.AngleNamedGroups |
        RegexFlavorFeatures.Lookahead |
        RegexFlavorFeatures.Lookbehind |
        RegexFlavorFeatures.UnicodeCategories |
        RegexFlavorFeatures.UnicodeCategoriesRequireUnicodeFlag |
        RegexFlavorFeatures.UnicodePropertyNames |
        RegexFlavorFeatures.ClassSetOperations |
        RegexFlavorFeatures.BareBraceIsLiteral);

    /// <summary>The PCRE and Perl flavor.</summary>
    public static RegexFlavor PcrePerl { get; } = new(
        "pcre",
        RegexFlavorFamily.Pcre,
        PerlFeatures |
        RegexFlavorFeatures.QuoteNamedGroups |
        RegexFlavorFeatures.PythonNamedGroups |
        RegexFlavorFeatures.AtomicGroups |
        RegexFlavorFeatures.PossessiveQuantifiers |
        RegexFlavorFeatures.Conditionals |
        RegexFlavorFeatures.BranchReset |
        RegexFlavorFeatures.Recursion |
        RegexFlavorFeatures.BacktrackingVerbs |
        RegexFlavorFeatures.InlineOptions |
        RegexFlavorFeatures.IgnorePatternWhitespace |
        RegexFlavorFeatures.CommentGroups |
        RegexFlavorFeatures.PosixBracketExpressions |
        RegexFlavorFeatures.QuotedLiterals |
        RegexFlavorFeatures.UnicodePropertyNames |
        RegexFlavorFeatures.StrictEscapes |
        RegexFlavorFeatures.AnchorsAZ |
        RegexFlavorFeatures.KeepOut);

    /// <summary>POSIX extended regular expressions (ERE).</summary>
    public static RegexFlavor PosixExtended { get; } = new(
        "ere",
        RegexFlavorFamily.Posix,
        ModernFeatures | RegexFlavorFeatures.PosixBracketExpressions);

    /// <summary>POSIX basic regular expressions (BRE), in which the delimiters are spelled with a backslash.</summary>
    /// <remarks>
    /// <c>\(…\)</c> groups, <c>\{n,m\}</c> bounds, and backreferences are all supported. Alternation with <c>\|</c>
    /// and the <c>\+</c> and <c>\?</c> quantifiers are GNU extensions rather than POSIX proper, and are accepted.
    /// </remarks>
    public static RegexFlavor PosixBasic { get; } = new(
        "bre",
        RegexFlavorFamily.Posix,
        CommonFeatures |
        RegexFlavorFeatures.PosixBracketExpressions |
        RegexFlavorFeatures.EscapedGroupDelimiters |
        RegexFlavorFeatures.Alternation |
        RegexFlavorFeatures.PlusAndQuestionQuantifiers);

    /// <summary>The canonical lowercase name of the flavor, such as <c>net</c> or <c>pcre</c>.</summary>
    public string Name { get; }

    /// <summary>The grammar family the flavor belongs to.</summary>
    public RegexFlavorFamily Family { get; }

    /// <summary>The optional constructs the flavor supports.</summary>
    public RegexFlavorFeatures Features { get; }

    /// <summary>Returns <see langword="true"/> when the flavor supports every feature in <paramref name="feature"/>.</summary>
    public bool HasFeature(RegexFlavorFeatures feature) => (Features & feature) == feature;

    /// <summary>Resolves a flavor from its name. Recognizes the canonical names and common aliases such as <c>dotnet</c>.</summary>
    public static bool TryParse(string? name, [NotNullWhen(true)] out RegexFlavor? flavor)
    {
        flavor = name?.Trim().ToLowerInvariant() switch
        {
            "net" or "dotnet" or ".net" or "csharp" => Net,
            "javascript" or "js" or "ecmascript" or "es" => JavaScript,
            "pcre" or "pcre2" or "perl" => PcrePerl,
            "ere" or "posix" or "posix-extended" or "egrep" => PosixExtended,
            "bre" or "posix-basic" or "grep" => PosixBasic,
            _ => null,
        };

        return flavor is not null;
    }

    public override string ToString() => Name;
}
