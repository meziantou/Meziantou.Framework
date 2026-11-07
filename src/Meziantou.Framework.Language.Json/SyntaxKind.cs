namespace Meziantou.Framework.Language.Json;

/// <summary>The kinds of node, token, and trivia a JSON tree is made of.</summary>
public enum SyntaxKind
{
    None = 0,

    /// <summary>A sequence of children held in one slot. Shared by every language, and so fixed at 1.</summary>
    List = 1,

    OpenBraceToken = 8000,
    CloseBraceToken = 8001,
    OpenBracketToken = 8002,
    CloseBracketToken = 8003,
    ColonToken = 8004,
    CommaToken = 8005,

    StringToken = 8100,
    NumberToken = 8101,
    TrueKeyword = 8102,
    FalseKeyword = 8103,
    NullKeyword = 8104,

    /// <summary>Text the parser could make no sense of.</summary>
    BadToken = 8500,

    EndOfFileToken = 8501,

    WhitespaceTrivia = 8600,
    EndOfLineTrivia = 8601,
    SingleLineCommentTrivia = 8602,
    MultiLineCommentTrivia = 8603,

    JsonDocument = 9000,
    JsonObject = 9001,
    JsonMember = 9002,
    JsonArray = 9003,
    JsonString = 9004,
    JsonNumber = 9005,
    JsonTrueLiteral = 9006,
    JsonFalseLiteral = 9007,
    JsonNullLiteral = 9008,
    JsonSkippedText = 9009,
}
