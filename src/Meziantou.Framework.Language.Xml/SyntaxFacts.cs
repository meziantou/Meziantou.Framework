using System.Text;

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
    /// <remarks>
    /// This is the <c>NameStartChar</c> production of XML 1.0 verbatim. It is not the same set as "a Unicode letter":
    /// it excludes some letters, such as <c>ª</c> and <c>µ</c>, and includes characters that are not letters at all.
    /// It takes a scalar value rather than a <see cref="char"/> because a name character may sit outside the basic
    /// plane, where it is written as a surrogate pair that means nothing one half at a time.
    /// </remarks>
    public static bool IsNameStartCharacter(Rune value) => value.Value switch
    {
        ':' or '_' => true,
        >= 'A' and <= 'Z' => true,
        >= 'a' and <= 'z' => true,
        >= 0xC0 and <= 0xD6 => true,
        >= 0xD8 and <= 0xF6 => true,
        >= 0xF8 and <= 0x2FF => true,
        >= 0x370 and <= 0x37D => true,
        >= 0x37F and <= 0x1FFF => true,
        >= 0x200C and <= 0x200D => true,
        >= 0x2070 and <= 0x218F => true,
        >= 0x2C00 and <= 0x2FEF => true,
        >= 0x3001 and <= 0xD7FF => true,
        >= 0xF900 and <= 0xFDCF => true,
        >= 0xFDF0 and <= 0xFFFD => true,
        >= 0x10000 and <= 0xEFFFF => true,
        _ => false,
    };

    /// <summary>Determines whether <paramref name="value"/> may appear inside an XML name.</summary>
    /// <remarks>
    /// This is the <c>NameChar</c> production of XML 1.0: everything a name may begin with, plus the characters that
    /// may only follow -- a digit, a hyphen, a full stop, the middle dot, and the combining marks.
    /// </remarks>
    public static bool IsNameCharacter(Rune value) => IsNameStartCharacter(value) || value.Value switch
    {
        '-' or '.' or 0xB7 => true,
        >= '0' and <= '9' => true,
        >= 0x300 and <= 0x36F => true,
        >= 0x203F and <= 0x2040 => true,
        _ => false,
    };
}
