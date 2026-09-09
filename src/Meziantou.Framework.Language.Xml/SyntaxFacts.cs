namespace Meziantou.Framework.Language.Xml;

/// <summary>Answers questions about a <see cref="SyntaxKind"/> that do not need a node to ask them of.</summary>
public static class SyntaxFacts
{
    /// <summary>Returns the text a kind is always spelled with, or an empty string when its text varies.</summary>
    /// <remarks>
    /// This is the canonical spelling. A document may write <c>&lt;?xml</c> and <c>&lt;!DOCTYPE</c> in any case, and
    /// the token then carries what the document said; a token built from a kind alone gets the spelling here.
    /// </remarks>
    public static string GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.LessThanToken => "<",
        SyntaxKind.GreaterThanToken => ">",
        SyntaxKind.LessThanSlashToken => "</",
        SyntaxKind.SlashGreaterThanToken => "/>",
        SyntaxKind.EqualsToken => "=",
        SyntaxKind.SingleQuoteToken => "'",
        SyntaxKind.DoubleQuoteToken => "\"",
        SyntaxKind.LessThanQuestionToken => "<?",
        SyntaxKind.LessThanQuestionXmlToken => "<?xml",
        SyntaxKind.QuestionGreaterThanToken => "?>",
        SyntaxKind.XmlCommentStartToken => "<!--",
        SyntaxKind.XmlCommentEndToken => "-->",
        SyntaxKind.CDataStartToken => "<![CDATA[",
        SyntaxKind.CDataEndToken => "]]>",
        SyntaxKind.DocumentTypeStartToken => "<!DOCTYPE",
        _ => "",
    };

    /// <summary>Determines whether <paramref name="kind"/> is whitespace or a line break.</summary>
    public static bool IsTrivia(SyntaxKind kind) => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia;

    /// <summary>Determines whether <paramref name="value"/> may begin an XML name.</summary>
    public static bool IsNameStartCharacter(char value) => char.IsLetter(value) || value is '_' or ':';

    /// <summary>Determines whether <paramref name="value"/> may appear inside an XML name.</summary>
    public static bool IsNameCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or ':' or '-' or '.';
}
