namespace Meziantou.Framework.Language.Toml;

/// <summary>The kinds of node, token, and trivia a TOML tree is made of.</summary>
public enum SyntaxKind
{
    None = 0,

    /// <summary>A sequence of children held in one slot. Shared by every language, and so fixed at 1.</summary>
    List = 1,

    OpenBracketToken = 8200,
    CloseBracketToken = 8201,
    EqualsToken = 8202,
    CommaToken = 8203,
    KeyToken = 8300,
    ValueToken = 8301,

    /// <summary>Text the parser could make no sense of.</summary>
    BadToken = 8500,

    EndOfFileToken = 8501,

    WhitespaceTrivia = 8600,
    EndOfLineTrivia = 8601,
    CommentTrivia = 8602,

    TomlDocument = 9200,
    TomlTable = 9201,
    TomlProperty = 9202,
    TomlSkippedText = 9203,
    TomlArray = 9204,
}
