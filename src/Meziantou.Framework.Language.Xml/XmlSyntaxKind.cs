namespace Meziantou.Framework.Language.Xml;

/// <summary>Identifies the kind of XML syntax node, token, or trivia.</summary>
/// <example>
/// <code>
/// if (node.Kind == XmlSyntaxKind.XmlElement)
/// {
///     // handle element
/// }
/// </code>
/// </example>
public enum XmlSyntaxKind
{
    None,
    XmlDocument,
    XmlElement,
    XmlEmptyElement,
    XmlEndTag,
    XmlAttribute,
    XmlText,
    XmlComment,
    XmlCDataSection,
    XmlDeclaration,
    XmlProcessingInstruction,
    XmlDocumentType,
    XmlSkippedText,
    IdentifierToken,
    AttributeValueToken,
    TextToken,
    CommentToken,
    CDataToken,
    ProcessingInstructionToken,
    DeclarationToken,
    DocumentTypeToken,
    SkippedTextToken,
    WhitespaceTrivia,
    EndOfLineTrivia,
    SkippedTextTrivia,
}
