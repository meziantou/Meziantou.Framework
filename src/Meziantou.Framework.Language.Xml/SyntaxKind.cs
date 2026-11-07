namespace Meziantou.Framework.Language.Xml;

/// <summary>Identifies what a node, token, or trivium in an XML tree is.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs" ref:region="SyntaxKind_CompareKind" ref:dedent="true">
/// var tree = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;item /&gt;&lt;item&gt;text&lt;/item&gt;&lt;/root&gt;");
///
/// var elementCount = 0;
/// foreach (var node in tree.GetRoot().DescendantNodes())
/// {
///     if (node.Kind() == SyntaxKind.XmlElement)
///     {
///         // handle element
///         elementCount++;
///     }
/// }
///
/// // &lt;item /&gt; is an XmlEmptyElement, so only &lt;root&gt; and the second &lt;item&gt; are counted
/// Assert.Equal(2, elementCount);
/// </code>
/// </example>
public enum SyntaxKind
{
    None = 0,

    /// <summary>A list of nodes or tokens. Reserved for the list node the shared layer builds.</summary>
    List = 1,

    // Nodes
    XmlDocument,
    XmlElement,
    XmlEmptyElement,
    XmlElementStartTag,
    XmlElementEndTag,
    XmlAttribute,
    XmlText,
    XmlComment,
    XmlCDataSection,
    XmlDeclaration,
    XmlProcessingInstruction,
    XmlDocumentType,
    XmlSkippedText,

    // Punctuation
    LessThanToken,
    GreaterThanToken,
    LessThanSlashToken,
    SlashGreaterThanToken,
    EqualsToken,
    SingleQuoteToken,
    DoubleQuoteToken,
    LessThanQuestionToken,
    LessThanQuestionXmlToken,
    QuestionGreaterThanToken,
    XmlCommentStartToken,
    XmlCommentEndToken,
    CDataStartToken,
    CDataEndToken,
    DocumentTypeStartToken,

    // Tokens carrying text
    IdentifierToken,
    AttributeValueToken,
    TextToken,
    CommentToken,
    CDataToken,
    ProcessingInstructionDataToken,
    DocumentTypeContentToken,
    BadToken,
    EndOfFileToken,

    // Trivia
    WhitespaceTrivia,
    EndOfLineTrivia,

    /// <summary>Text inside a tag that the parser could not read, kept so the document still reproduces its source.</summary>
    SkippedTextTrivia,
}
