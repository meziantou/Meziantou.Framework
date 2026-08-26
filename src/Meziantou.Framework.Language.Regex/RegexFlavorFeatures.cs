namespace Meziantou.Framework.Language.Regex;

/// <summary>The optional constructs a <see cref="RegexFlavor"/> supports.</summary>
/// <remarks>
/// A feature records a difference <em>within</em> a grammar family. Differences between families are handled by
/// <see cref="RegexFlavorFamily"/>, which selects the parser.
/// </remarks>
[Flags]
public enum RegexFlavorFeatures
{
    None = 0,

    /// <summary>Named groups in any spelling.</summary>
    NamedGroups = 1 << 0,

    /// <summary>Angle-bracket named groups, <c>(?&lt;name&gt;…)</c>.</summary>
    AngleNamedGroups = 1 << 1,

    /// <summary>Single-quoted named groups, <c>(?'name'…)</c>.</summary>
    QuoteNamedGroups = 1 << 2,

    /// <summary>Python-style named groups, <c>(?P&lt;name&gt;…)</c>.</summary>
    PythonNamedGroups = 1 << 3,

    /// <summary>Balancing groups, <c>(?&lt;current-previous&gt;…)</c>.</summary>
    BalancingGroups = 1 << 4,

    /// <summary>Atomic groups, <c>(?&gt;…)</c>.</summary>
    AtomicGroups = 1 << 5,

    /// <summary>Possessive quantifiers, <c>a*+</c>.</summary>
    PossessiveQuantifiers = 1 << 6,

    /// <summary>Lookbehind, <c>(?&lt;=…)</c> and <c>(?&lt;!…)</c>.</summary>
    Lookbehind = 1 << 7,

    /// <summary>Conditional alternations, <c>(?(1)yes|no)</c>.</summary>
    Conditionals = 1 << 8,

    /// <summary>Branch reset groups, <c>(?|…)</c>.</summary>
    BranchReset = 1 << 9,

    /// <summary>Recursion, <c>(?R)</c> and <c>(?1)</c>.</summary>
    Recursion = 1 << 10,

    /// <summary>Backtracking control verbs such as <c>(*SKIP)</c>.</summary>
    BacktrackingVerbs = 1 << 11,

    /// <summary>Inline options, <c>(?i)</c> and <c>(?i:…)</c>.</summary>
    InlineOptions = 1 << 12,

    /// <summary>Extended mode, in which whitespace and <c>#</c> comments are insignificant.</summary>
    IgnorePatternWhitespace = 1 << 13,

    /// <summary>Comment groups, <c>(?#…)</c>.</summary>
    CommentGroups = 1 << 14,

    /// <summary>Unicode categories and blocks, <c>\p{L}</c> and <c>\P{L}</c>.</summary>
    UnicodeCategories = 1 << 15,

    /// <summary>Unicode categories are recognized only when the pattern opts into Unicode mode.</summary>
    UnicodeCategoriesRequireUnicodeFlag = 1 << 16,

    /// <summary>Character class subtraction, <c>[a-z-[aeiou]]</c>.</summary>
    CharacterClassSubtraction = 1 << 17,

    /// <summary>Class set operations and nested classes, as JavaScript's <c>v</c> flag defines them.</summary>
    ClassSetOperations = 1 << 18,

    /// <summary>POSIX bracket expressions, <c>[[:alpha:]]</c>.</summary>
    PosixBracketExpressions = 1 << 19,

    /// <summary>Quoted literal runs, <c>\Q…\E</c>.</summary>
    QuotedLiterals = 1 << 20,

    /// <summary>The <c>\A</c>, <c>\Z</c>, <c>\z</c>, and <c>\G</c> anchors.</summary>
    AnchorsAZ = 1 << 21,

    /// <summary>The <c>\K</c> match reset.</summary>
    KeepOut = 1 << 22,

    /// <summary>Groups and bounds are written escaped, as POSIX basic expressions write <c>\(</c> and <c>\{</c>.</summary>
    EscapedGroupDelimiters = 1 << 23,

    /// <summary>A <c>{</c> that does not start a well-formed bound is an ordinary character rather than an error.</summary>
    BareBraceIsLiteral = 1 << 24,

    /// <summary>Alternation with <c>|</c>.</summary>
    Alternation = 1 << 25,

    /// <summary>The <c>+</c> and <c>?</c> quantifiers.</summary>
    PlusAndQuestionQuantifiers = 1 << 26,

    /// <summary>Lazy quantifiers, <c>a*?</c>.</summary>
    LazyQuantifiers = 1 << 27,

    /// <summary>Backreferences.</summary>
    Backreferences = 1 << 28,

    /// <summary>
    /// An unrecognized alphabetic escape such as <c>\q</c> is an error. Where the flavor lacks this, the escape simply
    /// stands for the character itself.
    /// </summary>
    StrictEscapes = 1 << 29,
}
