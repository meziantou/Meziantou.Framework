namespace Meziantou.Framework.Json.Internals;

internal enum JsonPathTokenKind
{
    EndOfInput,

    // Identifiers
    RootIdentifier,         // $
    CurrentNodeIdentifier,  // @

    // Punctuation
    Dot,                    // .
    DoubleDot,              // ..
    OpenBracket,            // [
    CloseBracket,           // ]
    OpenParen,              // (
    CloseParen,             // )
    Comma,                  // ,
    Colon,                  // :
    QuestionMark,           // ?
    Asterisk,               // *
    ExclamationMark,        // !

    // Comparison operators
    Equal,                  // ==
    NotEqual,               // !=
    LessThan,               // <
    LessThanOrEqual,        // <=
    GreaterThan,            // >
    GreaterThanOrEqual,     // >=

    // Logical operators
    And,                    // &&
    Or,                     // ||

    // Literals
    StringLiteral,          // 'string' or "string"
    NumberLiteral,          // 123 or -1 or 1.5e10

    // Keywords
    True,                   // true
    False,                  // false
    Null,                   // null

    // Identifiers (member name shorthand or function name)
    Identifier,
}
