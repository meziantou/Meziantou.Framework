using System.Security;
using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>Builds XML nodes, tokens, and trivia.</summary>
/// <remarks>
/// A node built here is not part of any document, so its span starts at zero. Putting it into a tree with
/// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/> gives it a real position,
/// without re-reading any text.
/// </remarks>
/// <example>
/// <code>
/// var node = SyntaxFactory.XmlEmptyElement("package", [SyntaxFactory.XmlAttribute("version", "1.0.0")]);
/// var document = SyntaxFactory.XmlDocument(node);
/// </code>
/// </example>
public static class SyntaxFactory
{
    /// <summary>A single space.</summary>
    public static SyntaxTrivia Space => Whitespace(" ");

    /// <summary>A single tab.</summary>
    public static SyntaxTrivia Tab => Whitespace("\t");

    /// <summary>A Unix line break.</summary>
    public static SyntaxTrivia LineFeed => Trivia(SyntaxKind.EndOfLineTrivia, "\n");

    /// <summary>A Windows line break.</summary>
    public static SyntaxTrivia CarriageReturnLineFeed => Trivia(SyntaxKind.EndOfLineTrivia, "\r\n");

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Whitespace(string text) => Trivia(SyntaxKind.WhitespaceTrivia, text);

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia EndOfLine(string text) => Trivia(SyntaxKind.EndOfLineTrivia, text);

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Trivia(SyntaxKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxTrivia(token: default, Green.SyntaxFactory.Trivia(kind, text), position: 0, index: 0);
    }

    /// <summary>Creates a token of <paramref name="kind"/> spelled <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Token(SyntaxKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.Token(leading: null, kind, text), position: 0, index: 0);
    }

    /// <summary>Creates a token of <paramref name="kind"/>, spelled the only way that kind can be.</summary>
    public static SyntaxToken Token(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.Token(kind), position: 0, index: 0);

    /// <summary>Creates a zero-width token standing in for one a document does not have.</summary>
    public static SyntaxToken MissingToken(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.MissingToken(kind), position: 0, index: 0);

    /// <summary>Creates a name token.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Identifier(string name) => Token(SyntaxKind.IdentifierToken, name);

    /// <summary>Creates an attribute value token, escaping what has to be escaped inside <paramref name="quote"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static SyntaxToken AttributeValue(string value, char quote = '"')
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = EscapeAttributeValue(value, quote);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.AttributeValueToken, text, value), position: 0, index: 0);
    }

    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static XmlDocumentSyntax XmlDocument(params XmlNodeSyntax[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return XmlDocument(new SyntaxList<XmlNodeSyntax>(nodes), Token(SyntaxKind.EndOfFileToken));
    }

    public static XmlDocumentSyntax XmlDocument(SyntaxList<XmlNodeSyntax> nodes, SyntaxToken endOfFileToken)
        => (XmlDocumentSyntax)new Green.XmlDocumentSyntax(nodes.Green, Required(endOfFileToken)).CreateRed();

    /// <summary>Creates an element with a start tag, content, and a matching end tag.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    public static XmlElementSyntax XmlElement(string name, params XmlNodeSyntax[] content) => XmlElement(name, [], content);

    /// <summary>Creates an element with a start tag, content, and a matching end tag.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="attributes"/>, or <paramref name="content"/> is <see langword="null"/>.</exception>
    public static XmlElementSyntax XmlElement(string name, IEnumerable<XmlAttributeSyntax> attributes, IEnumerable<XmlNodeSyntax> content)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(content);

        return XmlElement(
            XmlElementStartTag(name, attributes),
            new SyntaxList<XmlNodeSyntax>(content),
            XmlElementEndTag(name));
    }

    public static XmlElementSyntax XmlElement(XmlElementStartTagSyntax startTag, SyntaxList<XmlNodeSyntax> content, XmlElementEndTagSyntax? endTag)
    {
        ArgumentNullException.ThrowIfNull(startTag);

        return (XmlElementSyntax)new Green.XmlElementSyntax(startTag.Green, content.Green, endTag?.Green).CreateRed();
    }

    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="attributes"/> is <see langword="null"/>.</exception>
    public static XmlElementStartTagSyntax XmlElementStartTag(string name, IEnumerable<XmlAttributeSyntax> attributes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributes);

        return XmlElementStartTag(
            Token(SyntaxKind.LessThanToken),
            Identifier(name),
            new SyntaxList<XmlAttributeSyntax>(attributes.Select(SpaceBefore)),
            Token(SyntaxKind.GreaterThanToken));
    }

    public static XmlElementStartTagSyntax XmlElementStartTag(SyntaxToken lessThanToken, SyntaxToken nameToken, SyntaxList<XmlAttributeSyntax> attributes, SyntaxToken greaterThanToken)
        => (XmlElementStartTagSyntax)new Green.XmlElementStartTagSyntax(Required(lessThanToken), Required(nameToken), attributes.Green, Required(greaterThanToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static XmlElementEndTagSyntax XmlElementEndTag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return XmlElementEndTag(Token(SyntaxKind.LessThanSlashToken), Identifier(name), default, Token(SyntaxKind.GreaterThanToken));
    }

    public static XmlElementEndTagSyntax XmlElementEndTag(SyntaxToken lessThanSlashToken, SyntaxToken nameToken, SyntaxTokenList skippedTokens, SyntaxToken greaterThanToken)
        => (XmlElementEndTagSyntax)new Green.XmlElementEndTagSyntax(Required(lessThanSlashToken), Required(nameToken), skippedTokens.Node, Required(greaterThanToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static XmlEmptyElementSyntax XmlEmptyElement(string name) => XmlEmptyElement(name, []);

    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="attributes"/> is <see langword="null"/>.</exception>
    public static XmlEmptyElementSyntax XmlEmptyElement(string name, IEnumerable<XmlAttributeSyntax> attributes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributes);

        return XmlEmptyElement(
            Token(SyntaxKind.LessThanToken),
            Identifier(name),
            new SyntaxList<XmlAttributeSyntax>(attributes.Select(SpaceBefore)),
            Token(SyntaxKind.SlashGreaterThanToken).WithLeadingTrivia(Space));
    }

    public static XmlEmptyElementSyntax XmlEmptyElement(SyntaxToken lessThanToken, SyntaxToken nameToken, SyntaxList<XmlAttributeSyntax> attributes, SyntaxToken slashGreaterThanToken)
        => (XmlEmptyElementSyntax)new Green.XmlEmptyElementSyntax(Required(lessThanToken), Required(nameToken), attributes.Green, Required(slashGreaterThanToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public static XmlAttributeSyntax XmlAttribute(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        return XmlAttribute(
            Identifier(name),
            Token(SyntaxKind.EqualsToken),
            Token(SyntaxKind.DoubleQuoteToken),
            AttributeValue(value),
            Token(SyntaxKind.DoubleQuoteToken));
    }

    public static XmlAttributeSyntax XmlAttribute(SyntaxToken nameToken, SyntaxToken equalsToken, SyntaxToken startQuoteToken, SyntaxToken valueToken, SyntaxToken endQuoteToken)
        => (XmlAttributeSyntax)new Green.XmlAttributeSyntax(Required(nameToken), Required(equalsToken), Required(startQuoteToken), Required(valueToken), Required(endQuoteToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XmlTextSyntax XmlText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return XmlText(Token(SyntaxKind.TextToken, text));
    }

    public static XmlTextSyntax XmlText(SyntaxToken textToken) => (XmlTextSyntax)new Green.XmlTextSyntax(Required(textToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XmlCommentSyntax XmlComment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return XmlComment(Token(SyntaxKind.XmlCommentStartToken), Token(SyntaxKind.CommentToken, text), Token(SyntaxKind.XmlCommentEndToken));
    }

    public static XmlCommentSyntax XmlComment(SyntaxToken startCommentToken, SyntaxToken textToken, SyntaxToken endCommentToken)
        => (XmlCommentSyntax)new Green.XmlCommentSyntax(Required(startCommentToken), Required(textToken), Required(endCommentToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XmlCDataSectionSyntax XmlCDataSection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return XmlCDataSection(Token(SyntaxKind.CDataStartToken), Token(SyntaxKind.CDataToken, text), Token(SyntaxKind.CDataEndToken));
    }

    public static XmlCDataSectionSyntax XmlCDataSection(SyntaxToken startCDataToken, SyntaxToken textToken, SyntaxToken endCDataToken)
        => (XmlCDataSectionSyntax)new Green.XmlCDataSectionSyntax(Required(startCDataToken), Required(textToken), Required(endCDataToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static XmlDeclarationSyntax XmlDeclaration(string version = "1.0", string? encoding = null, string? standalone = null)
    {
        ArgumentNullException.ThrowIfNull(version);

        var attributes = new List<XmlAttributeSyntax> { SpaceBefore(XmlAttribute("version", version)) };
        if (!string.IsNullOrEmpty(encoding))
        {
            attributes.Add(SpaceBefore(XmlAttribute("encoding", encoding)));
        }

        if (!string.IsNullOrEmpty(standalone))
        {
            attributes.Add(SpaceBefore(XmlAttribute("standalone", standalone)));
        }

        return XmlDeclaration(
            Token(SyntaxKind.LessThanQuestionXmlToken, "<?xml"),
            new SyntaxList<XmlAttributeSyntax>(attributes),
            Token(SyntaxKind.QuestionGreaterThanToken));
    }

    public static XmlDeclarationSyntax XmlDeclaration(SyntaxToken startDeclarationToken, SyntaxList<XmlAttributeSyntax> attributes, SyntaxToken endDeclarationToken)
        => (XmlDeclarationSyntax)new Green.XmlDeclarationSyntax(Required(startDeclarationToken), attributes.Green, Required(endDeclarationToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static XmlProcessingInstructionSyntax XmlProcessingInstruction(string target, string? data)
    {
        ArgumentNullException.ThrowIfNull(target);

        return XmlProcessingInstruction(
            Token(SyntaxKind.LessThanQuestionToken),
            Identifier(target),
            ProcessingInstructionData(data),
            Token(SyntaxKind.QuestionGreaterThanToken));
    }

    public static XmlProcessingInstructionSyntax XmlProcessingInstruction(SyntaxToken startProcessingInstructionToken, SyntaxToken nameToken, SyntaxToken dataToken, SyntaxToken endProcessingInstructionToken)
        => (XmlProcessingInstructionSyntax)new Green.XmlProcessingInstructionSyntax(Required(startProcessingInstructionToken), Required(nameToken), Required(dataToken), Required(endProcessingInstructionToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static XmlDocumentTypeSyntax XmlDocumentType(string name, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Value holds everything the declaration carries, which begins with the name. A value that already starts
        // with the name is the whole content; one that does not is what follows it.
        var content = string.IsNullOrEmpty(value) ? "" : StartsWithName(value, name) ? value[name.Length..] : " " + value;

        return XmlDocumentType(
            Token(SyntaxKind.DocumentTypeStartToken, "<!DOCTYPE"),
            Identifier(name).WithLeadingTrivia(Space),
            Token(SyntaxKind.DocumentTypeContentToken, content),
            Token(SyntaxKind.GreaterThanToken));
    }

    public static XmlDocumentTypeSyntax XmlDocumentType(SyntaxToken startDocumentTypeToken, SyntaxToken nameToken, SyntaxToken contentToken, SyntaxToken greaterThanToken)
        => (XmlDocumentTypeSyntax)new Green.XmlDocumentTypeSyntax(Required(startDocumentTypeToken), Required(nameToken), Required(contentToken), Required(greaterThanToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XmlSkippedTextSyntax XmlSkippedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return XmlSkippedText(new SyntaxTokenList([Token(SyntaxKind.BadToken, text)]));
    }

    public static XmlSkippedTextSyntax XmlSkippedText(SyntaxTokenList tokens)
        => (XmlSkippedTextSyntax)new Green.XmlSkippedTextSyntax(tokens.Node).CreateRed();

    /// <summary>Determines whether <paramref name="value"/> begins with <paramref name="name"/> as a whole word.</summary>
    internal static bool StartsWithName(string value, string name)
    {
        if (name.Length == 0 || !value.StartsWith(name, StringComparison.Ordinal))
            return false;

        return value.Length == name.Length || char.IsWhiteSpace(value[name.Length]);
    }

    internal static SyntaxToken ProcessingInstructionData(string? data)
        => data is null ? MissingToken(SyntaxKind.ProcessingInstructionDataToken) : Token(SyntaxKind.ProcessingInstructionDataToken, " " + data);

    private static string EscapeAttributeValue(string value, char quote)
    {
        var escaped = SecurityElement.Escape(value) ?? "";

        // SecurityElement escapes both quote characters. Only the one the attribute is written with has to be.
        return quote == '\'' ? escaped.Replace("&quot;", "\"", StringComparison.Ordinal) : escaped.Replace("&apos;", "'", StringComparison.Ordinal);
    }

    private static XmlAttributeSyntax SpaceBefore(XmlAttributeSyntax attribute)
        => attribute.NameToken.HasLeadingTrivia ? attribute : attribute.WithLeadingTrivia(Space);

    /// <summary>Unwraps a token that a node requires, rejecting the default one no factory should produce.</summary>
    private static GreenNode Required(SyntaxToken token)
        => token.Node ?? throw new ArgumentException("A required token was not given.", nameof(token));
}
