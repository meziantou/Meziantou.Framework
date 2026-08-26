namespace Meziantou.Framework.Language.Regex;

/// <summary>The optional constructs a <see cref="RegexFlavor"/> supports.</summary>
/// <remarks>
/// <para>
/// A feature records a difference <em>within</em> a grammar family. Differences between families are handled by
/// <see cref="RegexFlavorFamily"/>, which selects the parser.
/// </para>
/// <para>
/// A construct the flavor does not have is not parsed as that construct. Where there is an ordinary reading it is used
/// instead -- <c>\A</c> is the letter <c>A</c> where there is no such anchor -- and where there is not, the construct
/// is reported as invalid.
/// </para>
/// </remarks>
[Flags]
public enum RegexFlavorFeatures : long
{
    None = 0,

    // ---- grouping ----

    /// <summary>
    /// The <c>(?…)</c> family of headers exists at all. POSIX has none of them, so a <c>?</c> after <c>(</c> is an
    /// ordinary quantifier with nothing to repeat rather than the start of a header.
    /// </summary>
    ExtendedGroupSyntax = 1L << 0,

    /// <summary>Non-capturing groups, <c>(?:…)</c>.</summary>
    NonCapturingGroups = 1L << 1,

    /// <summary>Named groups in any spelling.</summary>
    NamedGroups = 1L << 2,

    /// <summary>Angle-bracket named groups, <c>(?&lt;name&gt;…)</c>.</summary>
    AngleNamedGroups = 1L << 3,

    /// <summary>Single-quoted named groups, <c>(?'name'…)</c>.</summary>
    QuoteNamedGroups = 1L << 4,

    /// <summary>Python-style named groups, <c>(?P&lt;name&gt;…)</c>.</summary>
    PythonNamedGroups = 1L << 5,

    /// <summary>Balancing groups, <c>(?&lt;current-previous&gt;…)</c>.</summary>
    BalancingGroups = 1L << 6,

    /// <summary>Atomic groups, <c>(?&gt;…)</c>.</summary>
    AtomicGroups = 1L << 7,

    /// <summary>Branch reset groups, <c>(?|…)</c>.</summary>
    BranchReset = 1L << 8,

    /// <summary>Conditional alternations, <c>(?(1)yes|no)</c>.</summary>
    Conditionals = 1L << 9,

    /// <summary>Comment groups, <c>(?#…)</c>.</summary>
    CommentGroups = 1L << 10,

    /// <summary>Inline options, <c>(?i)</c> and <c>(?i:…)</c>.</summary>
    InlineOptions = 1L << 11,

    // ---- assertions ----

    /// <summary>Lookahead, <c>(?=…)</c> and <c>(?!…)</c>.</summary>
    Lookahead = 1L << 12,

    /// <summary>Lookbehind, <c>(?&lt;=…)</c> and <c>(?&lt;!…)</c>.</summary>
    Lookbehind = 1L << 13,

    /// <summary>The <c>\A</c>, <c>\Z</c>, <c>\z</c>, and <c>\G</c> anchors.</summary>
    AnchorsAZ = 1L << 14,

    /// <summary>The <c>\K</c> match reset.</summary>
    KeepOut = 1L << 15,

    // ---- repetition ----

    /// <summary>Alternation with <c>|</c>.</summary>
    Alternation = 1L << 16,

    /// <summary>The <c>+</c> and <c>?</c> quantifiers.</summary>
    PlusAndQuestionQuantifiers = 1L << 17,

    /// <summary>Lazy quantifiers, <c>a*?</c>.</summary>
    LazyQuantifiers = 1L << 18,

    /// <summary>Possessive quantifiers, <c>a*+</c>.</summary>
    PossessiveQuantifiers = 1L << 19,

    /// <summary>A <c>{</c> that does not start a well-formed bound is an ordinary character rather than an error.</summary>
    BareBraceIsLiteral = 1L << 20,

    // ---- escapes and classes ----

    /// <summary>Backreferences.</summary>
    Backreferences = 1L << 21,

    /// <summary>
    /// An unrecognized alphabetic escape such as <c>\q</c> is an error. Where the flavor lacks this, the escape stands
    /// for the character itself.
    /// </summary>
    StrictEscapes = 1L << 22,

    /// <summary>Quoted literal runs, <c>\Q…\E</c>.</summary>
    QuotedLiterals = 1L << 23,

    /// <summary>Unicode categories and blocks, <c>\p{L}</c> and <c>\P{L}</c>.</summary>
    UnicodeCategories = 1L << 24,

    /// <summary>Unicode categories are recognized only when the pattern opts into Unicode mode.</summary>
    UnicodeCategoriesRequireUnicodeFlag = 1L << 25,

    /// <summary>
    /// Unicode property names may name a property as well as a value, as <c>\p{Script=Greek}</c> does, and are not
    /// checked against the .NET category and block names.
    /// </summary>
    UnicodePropertyNames = 1L << 26,

    /// <summary>Character class subtraction, <c>[a-z-[aeiou]]</c>.</summary>
    CharacterClassSubtraction = 1L << 27,

    /// <summary>POSIX bracket expressions, <c>[[:alpha:]]</c>.</summary>
    PosixBracketExpressions = 1L << 28,

    /// <summary>
    /// The class set grammar: nested classes, <c>&amp;&amp;</c> and <c>--</c> operators, and <c>\q{…}</c> string
    /// disjunctions. JavaScript turns it on with the <c>v</c> flag rather than having it always.
    /// </summary>
    ClassSetOperations = 1L << 33,

    // ---- other ----

    /// <summary>Recursion, <c>(?R)</c> and <c>(?1)</c>.</summary>
    Recursion = 1L << 29,

    /// <summary>Backtracking control verbs such as <c>(*SKIP)</c>.</summary>
    BacktrackingVerbs = 1L << 30,

    /// <summary>Extended mode, in which whitespace and <c>#</c> comments are insignificant.</summary>
    IgnorePatternWhitespace = 1L << 31,

    /// <summary>Groups and bounds are written escaped, as POSIX basic expressions write <c>\(</c> and <c>\{</c>.</summary>
    EscapedGroupDelimiters = 1L << 32,
}
