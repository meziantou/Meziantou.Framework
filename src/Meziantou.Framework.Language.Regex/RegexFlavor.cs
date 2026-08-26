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
    /// The <c>u</c> flag is honoured: it makes a surrogate pair one atom. The <c>v</c> flag's class set operations --
    /// nested classes, <c>&amp;&amp;</c>, and <c>\q{…}</c> -- are not implemented, so a <c>v</c>-mode class is read as
    /// an ordinary one.
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

    /// <summary>POSIX basic regular expressions (BRE), in which groups and bounds are escaped.</summary>
    /// <remarks>
    /// The escaped forms <c>\(</c>, <c>\)</c>, <c>\{</c>, and <c>\}</c> are read as escapes rather than as grouping and
    /// bounds, so a basic expression round-trips but its groups are not in the tree as groups.
    /// </remarks>
    public static RegexFlavor PosixBasic { get; } = new(
        "bre",
        RegexFlavorFamily.Posix,
        CommonFeatures | RegexFlavorFeatures.PosixBracketExpressions | RegexFlavorFeatures.EscapedGroupDelimiters);

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
