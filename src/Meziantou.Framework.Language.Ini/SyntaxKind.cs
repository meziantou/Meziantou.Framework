namespace Meziantou.Framework.Language.Ini;

/// <summary>The kinds of node, token, and trivia an INI tree is made of.</summary>
public enum SyntaxKind
{
    None = 0,

    /// <summary>A sequence of children held in one slot. Shared by every language, and so fixed at 1.</summary>
    List = 1,

    OpenBracketToken = 8200,
    CloseBracketToken = 8201,
    EqualsToken = 8202,
    ColonToken = 8203,

    KeyToken = 8300,
    ValueToken = 8301,

    /// <summary>Text the parser could make no sense of.</summary>
    BadToken = 8500,

    EndOfFileToken = 8501,

    WhitespaceTrivia = 8600,
    EndOfLineTrivia = 8601,
    CommentTrivia = 8602,

    IniDocument = 9200,
    IniSection = 9201,
    IniProperty = 9202,
    IniSkippedText = 9203,
}
