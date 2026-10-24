using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

/// <summary>The kind of a token. The reserved words have their own kind only when they are written without escapes.</summary>
internal enum TokenKind : byte
{
    EndOfFile,

    /// <summary>
    /// An identifier name that is not a reserved word written without escapes: the identifiers, the contextual keywords
    /// (<c>let</c>, <c>async</c>, <c>yield</c>, <c>await</c>...) and the reserved words written with escapes (<see cref="TokenFlags.Escaped"/>).
    /// </summary>
    Identifier,
    PrivateName,
    NumericLiteral,
    BigIntLiteral,
    StringLiteral,

    /// <summary>A template part: <c>`...`</c>, <c>`...${</c>, <c>}...${</c> or <c>}...`</c> (<see cref="TokenFlags.TemplateTail"/>).</summary>
    Template,
    RegularExpression,

    // Punctuators
    LeftBrace,
    RightBrace,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Dot,
    Ellipsis,
    Semicolon,
    Comma,
    Question,
    QuestionDot,
    Colon,
    Arrow,
    LessThan,
    GreaterThan,
    LessThanEqual,
    GreaterThanEqual,
    EqualEqual,
    NotEqual,
    EqualEqualEqual,
    NotEqualEqual,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    StarStar,
    PlusPlus,
    MinusMinus,
    LeftShift,
    RightShift,
    UnsignedRightShift,
    Ampersand,
    Bar,
    Caret,
    Exclamation,
    Tilde,
    AmpersandAmpersand,
    BarBar,
    QuestionQuestion,
    Equal,
    PlusEqual,
    MinusEqual,
    StarEqual,
    SlashEqual,
    PercentEqual,
    StarStarEqual,
    LeftShiftEqual,
    RightShiftEqual,
    UnsignedRightShiftEqual,
    AmpersandEqual,
    BarEqual,
    CaretEqual,
    AmpersandAmpersandEqual,
    BarBarEqual,
    QuestionQuestionEqual,

    /// <summary><c>@</c>: the start of a decorator.</summary>
    At,

    // Reserved words
    Break,
    Case,
    Catch,
    Class,
    Const,
    Continue,
    Debugger,
    Default,
    Delete,
    Do,
    Else,
    Enum,
    Export,
    Extends,
    False,
    Finally,
    For,
    Function,
    If,
    Import,
    In,
    InstanceOf,
    New,
    Null,
    Return,
    Super,
    Switch,
    This,
    Throw,
    True,
    Try,
    TypeOf,
    Var,
    Void,
    While,
    With,
}

[Flags]
internal enum TokenFlags : byte
{
    None = 0,

    /// <summary>A line terminator precedes the token (in the white space or in a comment).</summary>
    NewLineBefore = 1 << 0,

    /// <summary>The identifier contains a Unicode escape sequence.</summary>
    Escaped = 1 << 1,

    /// <summary>
    /// A legacy octal literal (<c>017</c>), a decimal literal with a leading zero (<c>08</c>), or a string with a legacy
    /// octal escape (<c>\07</c>) or a non-octal decimal escape (<c>\8</c>): forbidden in strict mode code.
    /// </summary>
    LegacyOctal = 1 << 2,

    /// <summary>The template part has an invalid escape sequence: its cooked value is undefined.</summary>
    InvalidEscape = 1 << 3,

    /// <summary>The template part ends with a backtick.</summary>
    TemplateTail = 1 << 4,

    /// <summary>The string literal contains an escape sequence or a line continuation.</summary>
    HasEscape = 1 << 5,
}

/// <summary>A token scanned by the <see cref="Lexer"/>.</summary>
internal struct Token
{
    public TokenKind Kind;
    public TokenFlags Flags;
    public int Start;
    public int End;

    /// <summary>The name of an identifier or a private name, the value of a string, the cooked value of a template part, the pattern of a regular expression.</summary>
    public string? Value;

    /// <summary>The raw value of a template part, or the flags of a regular expression.</summary>
    public string? Raw;

    public double Number;
    public BigInteger BigInt;

    /// <summary>The offset of the invalid or legacy escape sequence of a string or a template part.</summary>
    public int EscapePosition;

    public readonly bool NewLineBefore => (Flags & TokenFlags.NewLineBefore) != 0;

    public readonly bool IsEscaped => (Flags & TokenFlags.Escaped) != 0;

    public readonly bool IsKeyword => Kind >= TokenKind.Break;
}
