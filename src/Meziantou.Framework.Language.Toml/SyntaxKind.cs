namespace Meziantou.Framework.Language.Toml;

/// <summary>The kinds of node, token, and trivia a TOML tree is made of.</summary>
public enum SyntaxKind
{
    None = 0,

    /// <summary>A sequence of children held in one slot. Shared by every language, and so fixed at 1.</summary>
    List = 1,

    /// <summary><c>[</c>, which opens a table header or an array.</summary>
    OpenBracketToken = 8200,

    /// <summary><c>]</c>, which closes a table header or an array.</summary>
    CloseBracketToken = 8201,

    EqualsToken = 8202,
    CommaToken = 8203,

    /// <summary><c>[[</c>, which opens an array-of-tables header.</summary>
    OpenBracketOpenBracketToken = 8204,

    /// <summary><c>]]</c>, which closes an array-of-tables header.</summary>
    CloseBracketCloseBracketToken = 8205,

    OpenBraceToken = 8206,
    CloseBraceToken = 8207,

    /// <summary><c>.</c>, which separates the parts of a dotted key.</summary>
    DotToken = 8208,

    /// <summary>A key made of <c>A-Za-z0-9_-</c> only.</summary>
    BareKeyToken = 8300,

    /// <summary>A string between double quotes, such as <c>"value"</c>.</summary>
    BasicStringToken = 8301,

    /// <summary>A string between three double quotes, which may span lines.</summary>
    MultiLineBasicStringToken = 8302,

    /// <summary>A string between single quotes, which has no escape sequences.</summary>
    LiteralStringToken = 8303,

    /// <summary>A string between three single quotes, which may span lines and has no escape sequences.</summary>
    MultiLineLiteralStringToken = 8304,

    IntegerToken = 8305,
    FloatToken = 8306,
    TrueKeyword = 8307,
    FalseKeyword = 8308,

    /// <summary>A date and time with an offset from UTC, such as <c>1979-05-27T07:32:00Z</c>.</summary>
    OffsetDateTimeToken = 8309,

    /// <summary>A date and time without an offset, such as <c>1979-05-27T07:32:00</c>.</summary>
    LocalDateTimeToken = 8310,

    /// <summary>A date without a time, such as <c>1979-05-27</c>.</summary>
    LocalDateToken = 8311,

    /// <summary>A time without a date, such as <c>07:32:00</c>.</summary>
    LocalTimeToken = 8312,

    /// <summary>Text the parser could make no sense of.</summary>
    BadToken = 8500,

    EndOfFileToken = 8501,

    WhitespaceTrivia = 8600,
    EndOfLineTrivia = 8601,
    CommentTrivia = 8602,

    TomlDocument = 9200,

    /// <summary>A table header such as <c>[server]</c>.</summary>
    TomlTable = 9201,

    /// <summary>A key/value pair such as <c>port = 8080</c>.</summary>
    TomlProperty = 9202,

    /// <summary>A line the parser could make no sense of.</summary>
    TomlSkippedText = 9203,

    TomlArray = 9204,

    /// <summary>An array-of-tables header such as <c>[[products]]</c>.</summary>
    TomlArrayOfTables = 9205,

    /// <summary>A key, dotted or not, such as <c>server.port</c>.</summary>
    TomlKey = 9206,

    TomlInlineTable = 9207,
    TomlString = 9208,
    TomlInteger = 9209,
    TomlFloat = 9210,
    TomlBoolean = 9211,
    TomlOffsetDateTime = 9212,
    TomlLocalDateTime = 9213,
    TomlLocalDate = 9214,
    TomlLocalTime = 9215,

    /// <summary>A value the parser could make no sense of, or one that is missing.</summary>
    TomlSkippedValue = 9216,
}
