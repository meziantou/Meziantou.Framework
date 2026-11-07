namespace Meziantou.Framework.Language.Toml;

/// <summary>Answers questions about TOML kinds that do not need a tree.</summary>
public static class SyntaxFacts
{
    /// <summary>Gets the text a kind is always spelled with, or an empty string when it varies.</summary>
    public static string GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenBracketToken => "[",
        SyntaxKind.CloseBracketToken => "]",
        SyntaxKind.OpenBracketOpenBracketToken => "[[",
        SyntaxKind.CloseBracketCloseBracketToken => "]]",
        SyntaxKind.OpenBraceToken => "{",
        SyntaxKind.CloseBraceToken => "}",
        SyntaxKind.EqualsToken => "=",
        SyntaxKind.CommaToken => ",",
        SyntaxKind.DotToken => ".",
        SyntaxKind.TrueKeyword => "true",
        SyntaxKind.FalseKeyword => "false",
        _ => "",
    };

    public static bool IsTrivia(SyntaxKind kind)
        => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia or SyntaxKind.CommentTrivia;

    public static bool IsAnyToken(SyntaxKind kind) => kind is >= SyntaxKind.OpenBracketToken and <= SyntaxKind.EndOfFileToken;

    public static bool IsPunctuation(SyntaxKind kind) => kind is >= SyntaxKind.OpenBracketToken and <= SyntaxKind.DotToken;

    public static bool IsEntry(SyntaxKind kind)
        => kind is SyntaxKind.TomlTable or SyntaxKind.TomlArrayOfTables or SyntaxKind.TomlProperty or SyntaxKind.TomlSkippedText;

    /// <summary>Determines whether a kind is one of the tokens that spell a string.</summary>
    public static bool IsStringToken(SyntaxKind kind)
        => kind is SyntaxKind.BasicStringToken or SyntaxKind.MultiLineBasicStringToken or SyntaxKind.LiteralStringToken or SyntaxKind.MultiLineLiteralStringToken;

    /// <summary>Determines whether a kind is one of the tokens a key can be made of.</summary>
    /// <remarks>Multi-line strings are strings, but not keys.</remarks>
    public static bool IsKeyToken(SyntaxKind kind)
        => kind is SyntaxKind.BareKeyToken or SyntaxKind.BasicStringToken or SyntaxKind.LiteralStringToken;

    /// <summary>Determines whether a kind is one of the tokens that spell a date, a time, or both.</summary>
    public static bool IsDateTimeToken(SyntaxKind kind)
        => kind is SyntaxKind.OffsetDateTimeToken or SyntaxKind.LocalDateTimeToken or SyntaxKind.LocalDateToken or SyntaxKind.LocalTimeToken;

    /// <summary>Determines whether a kind is one of the nodes that can appear where a value is expected.</summary>
    public static bool IsValue(SyntaxKind kind) => kind is >= SyntaxKind.TomlString and <= SyntaxKind.TomlSkippedValue or SyntaxKind.TomlArray or SyntaxKind.TomlInlineTable;

    /// <summary>Gets the kind of the value node that wraps a token, or <see cref="SyntaxKind.None"/> when no value node does.</summary>
    public static SyntaxKind GetValueKind(SyntaxKind tokenKind) => tokenKind switch
    {
        SyntaxKind.BasicStringToken or SyntaxKind.MultiLineBasicStringToken or SyntaxKind.LiteralStringToken or SyntaxKind.MultiLineLiteralStringToken => SyntaxKind.TomlString,
        SyntaxKind.IntegerToken => SyntaxKind.TomlInteger,
        SyntaxKind.FloatToken => SyntaxKind.TomlFloat,
        SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword => SyntaxKind.TomlBoolean,
        SyntaxKind.OffsetDateTimeToken => SyntaxKind.TomlOffsetDateTime,
        SyntaxKind.LocalDateTimeToken => SyntaxKind.TomlLocalDateTime,
        SyntaxKind.LocalDateToken => SyntaxKind.TomlLocalDate,
        SyntaxKind.LocalTimeToken => SyntaxKind.TomlLocalTime,
        _ => SyntaxKind.None,
    };

    /// <summary>Determines whether <paramref name="text"/> can be written as a key without quotes.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static bool IsBareKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
            return false;

        foreach (var character in text)
        {
            if (!IsBareKeyCharacter(character))
                return false;
        }

        return true;
    }

    internal static bool IsBareKeyCharacter(char value) => value is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-';
}
