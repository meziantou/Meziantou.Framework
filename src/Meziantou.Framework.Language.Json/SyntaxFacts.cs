namespace Meziantou.Framework.Language.Json;

/// <summary>Answers questions about JSON kinds that do not need a tree.</summary>
public static class SyntaxFacts
{
    /// <summary>Gets the text a kind is always spelled with, or an empty string when it varies.</summary>
    public static string GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenBraceToken => "{",
        SyntaxKind.CloseBraceToken => "}",
        SyntaxKind.OpenBracketToken => "[",
        SyntaxKind.CloseBracketToken => "]",
        SyntaxKind.ColonToken => ":",
        SyntaxKind.CommaToken => ",",
        SyntaxKind.TrueKeyword => "true",
        SyntaxKind.FalseKeyword => "false",
        SyntaxKind.NullKeyword => "null",
        _ => "",
    };

    public static bool IsTrivia(SyntaxKind kind)
        => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia or SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia;

    public static bool IsAnyToken(SyntaxKind kind) => kind is >= SyntaxKind.OpenBraceToken and <= SyntaxKind.EndOfFileToken;

    public static bool IsPunctuation(SyntaxKind kind) => kind is >= SyntaxKind.OpenBraceToken and <= SyntaxKind.CommaToken;

    public static bool IsKeyword(SyntaxKind kind) => kind is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword;

    /// <summary>Determines whether a kind is one of the nodes that can appear where a value is expected.</summary>
    public static bool IsValue(SyntaxKind kind)
        => kind is SyntaxKind.JsonObject or SyntaxKind.JsonArray or SyntaxKind.JsonString or SyntaxKind.JsonNumber or SyntaxKind.JsonSkippedText || IsLiteralExpression(kind);

    public static bool IsLiteralExpression(SyntaxKind kind)
        => kind is SyntaxKind.JsonTrueLiteral or SyntaxKind.JsonFalseLiteral or SyntaxKind.JsonNullLiteral;

    /// <summary>Gets the keyword <paramref name="text"/> spells, or <see cref="SyntaxKind.None"/> when it spells none.</summary>
    public static SyntaxKind GetKeywordKind(string text) => text switch
    {
        "true" => SyntaxKind.TrueKeyword,
        "false" => SyntaxKind.FalseKeyword,
        "null" => SyntaxKind.NullKeyword,
        _ => SyntaxKind.None,
    };

    /// <summary>Gets the node kind that wraps a keyword token.</summary>
    public static SyntaxKind GetLiteralExpression(SyntaxKind tokenKind) => tokenKind switch
    {
        SyntaxKind.TrueKeyword => SyntaxKind.JsonTrueLiteral,
        SyntaxKind.FalseKeyword => SyntaxKind.JsonFalseLiteral,
        SyntaxKind.NullKeyword => SyntaxKind.JsonNullLiteral,
        _ => SyntaxKind.None,
    };
}
