namespace Meziantou.Framework.Language.Ini;

/// <summary>Answers questions about INI kinds that do not need a tree.</summary>
public static class SyntaxFacts
{
    /// <summary>Gets the text a kind is always spelled with, or an empty string when it varies.</summary>
    public static string GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenBracketToken => "[",
        SyntaxKind.CloseBracketToken => "]",
        SyntaxKind.EqualsToken => "=",
        SyntaxKind.ColonToken => ":",
        _ => "",
    };

    public static bool IsTrivia(SyntaxKind kind)
        => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia or SyntaxKind.CommentTrivia;

    public static bool IsAnyToken(SyntaxKind kind) => kind is >= SyntaxKind.OpenBracketToken and <= SyntaxKind.EndOfFileToken;

    public static bool IsPunctuation(SyntaxKind kind) => kind is >= SyntaxKind.OpenBracketToken and <= SyntaxKind.ColonToken;

    public static bool IsEntry(SyntaxKind kind)
        => kind is SyntaxKind.IniSection or SyntaxKind.IniProperty or SyntaxKind.IniSkippedText;

    /// <summary>Determines whether <paramref name="text"/> can be written as a key, so that it reads back as that key.</summary>
    /// <param name="text">The key.</param>
    /// <param name="options">
    /// The options of the document it goes into, as <see cref="SyntaxFactory.Key(string, IniParseOptions)"/> takes them, or
    /// <see langword="null"/> to check it as <see cref="SyntaxFactory.Key(string)"/> does, whatever the options.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static bool IsValidKey(string text, IniParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return SyntaxFactory.TryKey(text, options ?? SyntaxFactory.StrictOptions) is not null;
    }

    /// <summary>Determines whether <paramref name="text"/> can be written as a section name, so that it reads back as that name.</summary>
    /// <param name="text">The section name.</param>
    /// <param name="options">
    /// The options of the document it goes into, as <see cref="SyntaxFactory.SectionName(string, IniParseOptions)"/> takes
    /// them, or <see langword="null"/> to check it as <see cref="SyntaxFactory.SectionName(string)"/> does, whatever the options.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static bool IsValidSectionName(string text, IniParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return SyntaxFactory.TrySectionName(text, options ?? SyntaxFactory.StrictOptions) is not null;
    }

    /// <summary>Determines whether <paramref name="value"/> can be written as a value, quoted if need be, so that it reads back as that value.</summary>
    /// <remarks>
    /// With <paramref name="options"/>, a value with line breaks can be written when they allow multiline values, as
    /// <see cref="SyntaxFactory.IniProperty(string, string, IniParseOptions)"/> writes it. A value is always written in front
    /// of a line break; in front of the comment of an existing line, <see cref="IniPropertySyntax.WithValue(string)"/> may
    /// still refuse it.
    /// </remarks>
    /// <param name="value">The value.</param>
    /// <param name="options">
    /// The options of the document it goes into, or <see langword="null"/> to check it as <see cref="SyntaxFactory.Value(string)"/>
    /// does, whatever the options.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static bool IsValidValue(string value, IniParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (options is null)
            return SyntaxFactory.ValueLine(value, SyntaxFactory.StrictOptions, isContinuation: false) is not null;

        return SyntaxFactory.TryValueLines(value, options, "    ", SyntaxFactory.LineFeed, out _) is not null;
    }
}
