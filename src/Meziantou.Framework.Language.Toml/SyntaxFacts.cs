namespace Meziantou.Framework.Language.Toml;

/// <summary>Answers questions about TOML kinds that do not need a tree.</summary>
public static class SyntaxFacts
{
    /// <summary>Gets the text a kind is always spelled with, or an empty string when it varies.</summary>
    public static string GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenBracketToken => "[",
        SyntaxKind.CloseBracketToken => "]",
        SyntaxKind.EqualsToken => "=",
        _ => "",
    };

    public static bool IsTrivia(SyntaxKind kind)
        => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia or SyntaxKind.CommentTrivia;

    public static bool IsAnyToken(SyntaxKind kind) => kind is >= SyntaxKind.OpenBracketToken and <= SyntaxKind.EndOfFileToken;

    public static bool IsPunctuation(SyntaxKind kind) => kind is >= SyntaxKind.OpenBracketToken and <= SyntaxKind.EqualsToken;

    public static bool IsEntry(SyntaxKind kind)
        => kind is SyntaxKind.TomlTable or SyntaxKind.TomlProperty or SyntaxKind.TomlSkippedText;
}
