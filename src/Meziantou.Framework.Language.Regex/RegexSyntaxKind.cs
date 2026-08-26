namespace Meziantou.Framework.Language.Regex;

/// <summary>Identifies the kind of regular-expression syntax node, token, or trivia.</summary>
public enum RegexSyntaxKind
{
    None,

    // ---- Structure ----
    Pattern,
    Alternation,
    Sequence,
    Quantified,
    SimpleQuantifier,
    RangeQuantifier,
    SkippedText,

    // ---- Atoms ----
    Literal,
    CharacterEscape,
    CharacterClassEscape,
    UnicodeCategory,
    AnyCharacter,
    Anchor,
    QuotedLiteral,
    Backreference,
    NamedBackreference,
    InlineOptions,
    Recursion,
    BacktrackingVerb,

    // ---- Character classes ----
    CharacterClass,
    CharacterRange,
    PosixCharacterClass,
    CollatingElement,
    ClassSubtraction,
    ClassSetOperation,
    ClassStringLiteral,

    // ---- Groups ----
    CapturingGroup,
    NamedGroup,
    BalancingGroup,
    NonCapturingGroup,
    AtomicGroup,
    OptionsGroup,
    BranchResetGroup,
    Lookaround,
    Conditional,
    ConditionalReference,

    // ---- Tokens ----
    /// <summary>A single ordinary character that matches itself.</summary>
    LiteralToken,

    /// <summary>A complete character escape such as <c>\n</c>, <c>\x41</c>, <c>A</c>, <c>\cA</c>, or <c>\052</c>.</summary>
    EscapeToken,

    /// <summary>A shorthand class escape: <c>\d</c>, <c>\D</c>, <c>\w</c>, <c>\W</c>, <c>\s</c>, or <c>\S</c>.</summary>
    ClassEscapeToken,

    /// <summary>An anchor: <c>^</c>, <c>$</c>, <c>\b</c>, <c>\B</c>, <c>\A</c>, <c>\Z</c>, <c>\z</c>, <c>\G</c>, or <c>\K</c>.</summary>
    AnchorToken,
    DotToken,
    BarToken,
    AsteriskToken,
    PlusToken,
    QuestionToken,
    OpenBraceToken,
    CloseBraceToken,
    CommaToken,

    /// <summary>The digits of a <c>{n,m}</c> bound.</summary>
    NumberToken,
    OpenParenToken,
    CloseParenToken,
    OpenBracketToken,
    CloseBracketToken,

    /// <summary>The <c>^</c> that negates a character class.</summary>
    CaretToken,

    /// <summary>The <c>-</c> of a character range or of a .NET class subtraction.</summary>
    HyphenToken,

    /// <summary>The marker that follows <c>(</c> and selects the group kind, such as <c>?:</c>, <c>?&gt;</c>, or <c>?&lt;=</c>.</summary>
    GroupKindToken,

    /// <summary>An inline option run such as <c>imnsx-imnsx</c>.</summary>
    OptionsToken,

    /// <summary>The <c>:</c> that ends the header of an options group.</summary>
    ColonToken,

    /// <summary>A capture-group or backreference name.</summary>
    NameToken,

    /// <summary>The <c>&lt;</c> or <c>'</c> that introduces a name.</summary>
    OpenNameToken,

    /// <summary>The <c>&gt;</c> or <c>'</c> that ends a name.</summary>
    CloseNameToken,

    /// <summary>A numbered backreference such as <c>\1</c>.</summary>
    BackreferenceToken,

    /// <summary>The <c>\k</c> that introduces a named backreference.</summary>
    NamedBackreferenceStartToken,

    /// <summary>The <c>\p</c> or <c>\P</c> that introduces a Unicode category or block.</summary>
    CategoryStartToken,
    CategoryNameToken,

    /// <summary>The <c>\Q</c> that starts a quoted literal run.</summary>
    QuoteStartToken,
    QuoteTextToken,

    /// <summary>The <c>\E</c> that ends a quoted literal run.</summary>
    QuoteEndToken,

    /// <summary>The <c>[:</c>, <c>[.</c>, or <c>[=</c> that opens a POSIX bracket expression.</summary>
    PosixClassStartToken,
    PosixClassNameToken,

    /// <summary>The <c>:]</c>, <c>.]</c>, or <c>=]</c> that closes a POSIX bracket expression.</summary>
    PosixClassEndToken,

    /// <summary>A JavaScript <c>v</c>-mode class set operator: <c>&amp;&amp;</c>, <c>--</c>, or <c>||</c>.</summary>
    ClassSetOperatorToken,

    /// <summary>The body of a PCRE recursion construct such as <c>R</c> or <c>1</c> in <c>(?R)</c> and <c>(?1)</c>.</summary>
    RecursionToken,

    /// <summary>A PCRE backtracking verb such as <c>*SKIP</c>.</summary>
    VerbToken,

    /// <summary>The <c>/</c> delimiter of a JavaScript regular-expression literal.</summary>
    SlashToken,

    /// <summary>The flag letters that follow a JavaScript regular-expression literal.</summary>
    FlagsToken,

    /// <summary>Text the parser could not recognize. Kept so the pattern still round-trips.</summary>
    BadToken,
    EndOfPatternToken,

    // ---- Trivia ----
    /// <summary>Whitespace that is insignificant because extended mode is in effect.</summary>
    WhitespaceTrivia,

    /// <summary>An extended-mode <c>#</c> comment, up to but not including the terminating line feed.</summary>
    PatternCommentTrivia,

    /// <summary>A <c>(?#…)</c> comment. Recognized in every mode, not only in extended mode.</summary>
    InlineCommentTrivia,
}
