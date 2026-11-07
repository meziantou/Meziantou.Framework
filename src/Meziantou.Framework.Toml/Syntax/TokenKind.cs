namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// An enumeration to categorize tokens.
/// </summary>
#pragma warning disable CA1720 // The members are named after the TOML value types
public enum TokenKind
{
    /// <summary>Invalid token.</summary>
    Invalid,

    /// <summary>End of file token.</summary>
    Eof,

    /// <summary>Whitespace token.</summary>
    Whitespaces,

    /// <summary>New line token.</summary>
    NewLine,

    /// <summary>Comment token.</summary>
    Comment,

    /// <summary>Offset date-time with Z suffix.</summary>
    OffsetDateTimeByZ,
    /// <summary>Offset date-time with numeric offset.</summary>
    OffsetDateTimeByNumber,
    /// <summary>Local date-time token.</summary>
    LocalDateTime,
    /// <summary>Local date token.</summary>
    LocalDate,
    /// <summary>Local time token.</summary>
    LocalTime,

    /// <summary>Integer token.</summary>
    Integer,

    /// <summary>Hexadecimal integer token.</summary>
    IntegerHexa,

    /// <summary>Octal integer token.</summary>
    IntegerOctal,

    /// <summary>Binary integer token.</summary>
    IntegerBinary,

    /// <summary>Floating-point token.</summary>
    Float,

    /// <summary>String token.</summary>
    String,

    /// <summary>Multi-line string token.</summary>
    StringMulti,

    /// <summary>Literal string token.</summary>
    StringLiteral,

    /// <summary>Multi-line literal string token.</summary>
    StringLiteralMulti,

    /// <summary>Comma token.</summary>
    Comma,

    /// <summary>Dot token.</summary>
    Dot,

    /// <summary>Equal token.</summary>
    Equal,

    /// <summary>Open bracket token.</summary>
    OpenBracket,
    /// <summary>Open double bracket token.</summary>
    OpenBracketDouble,
    /// <summary>Close bracket token.</summary>
    CloseBracket,
    /// <summary>Close double bracket token.</summary>
    CloseBracketDouble,

    /// <summary>Open brace token.</summary>
    OpenBrace,
    /// <summary>Close brace token.</summary>
    CloseBrace,

    /// <summary>True literal token.</summary>
    True,
    /// <summary>False literal token.</summary>
    False,

    /// <summary>Infinity token.</summary>
    Infinite,
    /// <summary>Positive infinity token.</summary>
    PositiveInfinite,
    /// <summary>Negative infinity token.</summary>
    NegativeInfinite,

    /// <summary>NaN token.</summary>
    Nan,
    /// <summary>Positive NaN token.</summary>
    PositiveNan,
    /// <summary>Negative NaN token.</summary>
    NegativeNan,

    /// <summary>Basic key token.</summary>
    BasicKey,
}
#pragma warning restore CA1720
