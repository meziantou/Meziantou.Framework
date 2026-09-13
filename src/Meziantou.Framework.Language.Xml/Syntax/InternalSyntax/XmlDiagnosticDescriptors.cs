namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Everything the parser can report, grouped by id.</summary>
/// <remarks>
/// An id names a family of related problems, so a tool can filter on it without depending on the wording; each
/// descriptor within a family says precisely what went wrong.
/// </remarks>
internal static class XmlDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MissingEndTag = Error("XML0001", "Missing end tag", "Missing end tag for '{0}'.");

    public static readonly DiagnosticDescriptor UnexpectedEndTag = Error("XML0002", "Unexpected end tag", "Unexpected end tag '{0}'.");
    public static readonly DiagnosticDescriptor MismatchedEndTag = Error("XML0002", "Mismatched end tag", "End tag '{0}' does not match the start tag '{1}'.");
    public static readonly DiagnosticDescriptor MissingEndTagName = Error("XML0002", "Missing end tag name", "Expected an element name in the end tag.");
    public static readonly DiagnosticDescriptor WhitespaceBeforeEndTagName = Error("XML0002", "Whitespace before end tag name", "An end tag cannot have whitespace between '</' and the element name.");
    public static readonly DiagnosticDescriptor UnexpectedTextInEndTag = Error("XML0002", "Unexpected text in end tag", "Unexpected '{0}' in the end tag of '{1}'.");
    public static readonly DiagnosticDescriptor UnterminatedEndTag = Error("XML0002", "Unterminated end tag", "The end tag of '{0}' is missing its closing '>'.");

    public static readonly DiagnosticDescriptor InvalidCharacter = Error("XML0003", "Invalid character", "Character U+{0} is not allowed in XML.");

    public static readonly DiagnosticDescriptor MalformedCharacterReference = Error("XML0004", "Malformed character reference", "'{0}' is not a well-formed character reference.");
    public static readonly DiagnosticDescriptor InvalidCharacterReference = Error("XML0004", "Invalid character reference", "'{0}' does not refer to a character XML allows.");
    public static readonly DiagnosticDescriptor UnescapedAmpersand = Error("XML0004", "Unescaped ampersand", "The '&' character must be escaped as '&amp;' when it does not start a reference.");
    public static readonly DiagnosticDescriptor UndeclaredEntity = Error("XML0004", "Undeclared entity", "Reference to undeclared entity '{0}'.");

    public static readonly DiagnosticDescriptor LessThanInAttributeValue = Error("XML0005", "Unescaped '<' in attribute value", "The '<' character must be escaped as '&lt;' in an attribute value.");
    public static readonly DiagnosticDescriptor CDataEndInText = Error("XML0005", "']]>' in character data", "']]>' is not allowed in character data; escape the '>' as '&gt;'.");

    public static readonly DiagnosticDescriptor DuplicateAttribute = Error("XML0006", "Duplicate attribute", "Duplicate attribute '{0}'.");
    public static readonly DiagnosticDescriptor DuplicateExpandedAttribute = Error("XML0006", "Duplicate attribute", "Attributes '{0}' and '{1}' have the same namespace and local name.");

    public static readonly DiagnosticDescriptor UnterminatedDeclaration = Error("XML0007", "Unterminated XML declaration", "Unterminated XML declaration.");
    public static readonly DiagnosticDescriptor UnterminatedComment = Error("XML0008", "Unterminated comment", "Unterminated XML comment.");
    public static readonly DiagnosticDescriptor UnterminatedCData = Error("XML0009", "Unterminated CDATA section", "Unterminated CDATA section.");

    public static readonly DiagnosticDescriptor InvalidStartTag = Error("XML0010", "Invalid start tag", "Invalid start tag.");
    public static readonly DiagnosticDescriptor UnescapedLessThan = Error("XML0010", "Unescaped '<'", "The '<' character must be escaped as '&lt;' in character data.");
    public static readonly DiagnosticDescriptor UnexpectedTextInStartTag = Error("XML0010", "Unexpected text in start tag", "Unexpected '{0}' in the start tag of '{1}'.");
    public static readonly DiagnosticDescriptor UnterminatedStartTag = Error("XML0010", "Unterminated start tag", "The start tag of '{0}' is missing its closing '>'.");
    public static readonly DiagnosticDescriptor MissingAttributeValue = Error("XML0010", "Missing attribute value", "Attribute '{0}' is missing its value.");
    public static readonly DiagnosticDescriptor UnquotedAttributeValue = Error("XML0010", "Unquoted attribute value", "The value of attribute '{0}' must be quoted.");
    public static readonly DiagnosticDescriptor UnterminatedAttributeValue = Error("XML0010", "Unterminated attribute value", "The value of attribute '{0}' is missing its closing quote.");
    public static readonly DiagnosticDescriptor MissingWhitespaceBeforeAttribute = Error("XML0010", "Missing whitespace before attribute", "Attribute '{0}' must be separated from what precedes it by whitespace.");

    public static readonly DiagnosticDescriptor UnterminatedDocumentType = Error("XML0011", "Unterminated document type declaration", "Unterminated document type declaration.");
    public static readonly DiagnosticDescriptor UnterminatedProcessingInstruction = Error("XML0012", "Unterminated processing instruction", "Unterminated processing instruction.");

    public static readonly DiagnosticDescriptor MisplacedDeclaration = Error("XML0013", "Misplaced XML declaration", "The XML declaration is only allowed at the very start of the document.");
    public static readonly DiagnosticDescriptor DeclarationCase = Error("XML0013", "Invalid XML declaration", "The XML declaration must be written '<?xml', not '{0}'.");
    public static readonly DiagnosticDescriptor MissingVersion = Error("XML0013", "Missing XML version", "The XML declaration must start with a version.");
    public static readonly DiagnosticDescriptor InvalidVersion = Error("XML0013", "Invalid XML version", "'{0}' is not a valid XML version.");
    public static readonly DiagnosticDescriptor InvalidEncoding = Error("XML0013", "Invalid encoding name", "'{0}' is not a valid encoding name.");
    public static readonly DiagnosticDescriptor InvalidStandalone = Error("XML0013", "Invalid standalone value", "The standalone value must be 'yes' or 'no', not '{0}'.");
    public static readonly DiagnosticDescriptor UnexpectedDeclarationAttribute = Error("XML0013", "Unexpected XML declaration attribute", "'{0}' is not allowed here; the XML declaration takes version, encoding and standalone, once each and in that order.");
    public static readonly DiagnosticDescriptor UnexpectedTextInDeclaration = Error("XML0013", "Unexpected text in XML declaration", "Unexpected '{0}' in the XML declaration.");
    public static readonly DiagnosticDescriptor DeclarationMissingAttributeValue = Error("XML0013", "Missing attribute value", "Attribute '{0}' is missing its value.");
    public static readonly DiagnosticDescriptor DeclarationUnquotedAttributeValue = Error("XML0013", "Unquoted attribute value", "The value of attribute '{0}' must be quoted.");
    public static readonly DiagnosticDescriptor DeclarationUnterminatedAttributeValue = Error("XML0013", "Unterminated attribute value", "The value of attribute '{0}' is missing its closing quote.");
    public static readonly DiagnosticDescriptor DeclarationMissingWhitespaceBeforeAttribute = Error("XML0013", "Missing whitespace before attribute", "Attribute '{0}' must be separated from what precedes it by whitespace.");

    public static readonly DiagnosticDescriptor MissingRootElement = Error("XML0014", "Missing root element", "The document has no root element.");
    public static readonly DiagnosticDescriptor MultipleRootElements = Error("XML0014", "Multiple root elements", "A document can have only one root element; '{0}' is a second one.");
    public static readonly DiagnosticDescriptor TextOutsideRootElement = Error("XML0014", "Text outside the root element", "Text is not allowed outside the root element.");
    public static readonly DiagnosticDescriptor CDataOutsideRootElement = Error("XML0014", "CDATA section outside the root element", "A CDATA section is not allowed outside the root element.");
    public static readonly DiagnosticDescriptor DocumentTypeAfterRootElement = Error("XML0014", "Misplaced document type declaration", "The document type declaration must come before the root element.");
    public static readonly DiagnosticDescriptor DuplicateDocumentType = Error("XML0014", "Duplicate document type declaration", "A document can have only one document type declaration.");
    public static readonly DiagnosticDescriptor DocumentTypeInsideElement = Error("XML0014", "Misplaced document type declaration", "A document type declaration is not allowed inside an element.");

    public static readonly DiagnosticDescriptor DoubleHyphenInComment = Error("XML0015", "'--' in comment", "'--' is not allowed inside a comment.");
    public static readonly DiagnosticDescriptor HyphenBeforeCommentEnd = Error("XML0015", "Comment ending with '--->'", "A comment cannot end with '--->'.");

    public static readonly DiagnosticDescriptor MissingProcessingInstructionTarget = Error("XML0016", "Missing processing instruction target", "A processing instruction must start with a target name.");
    public static readonly DiagnosticDescriptor ReservedProcessingInstructionTarget = Error("XML0016", "Reserved processing instruction target", "The processing instruction target '{0}' is reserved.");
    public static readonly DiagnosticDescriptor MissingWhitespaceAfterTarget = Error("XML0016", "Missing whitespace after target", "The target of a processing instruction must be followed by whitespace.");

    public static readonly DiagnosticDescriptor UndeclaredPrefix = Error("XML0017", "Undeclared namespace prefix", "The namespace prefix '{0}' is not declared.");
    public static readonly DiagnosticDescriptor InvalidQualifiedName = Error("XML0017", "Invalid qualified name", "'{0}' is not a valid qualified name.");
    public static readonly DiagnosticDescriptor EmptyPrefixedNamespace = Error("XML0017", "Empty namespace for a prefix", "The prefix '{0}' cannot be bound to an empty namespace.");
    public static readonly DiagnosticDescriptor XmlPrefixBinding = Error("XML0017", "Invalid 'xml' prefix binding", "The 'xml' prefix can only be bound to 'http://www.w3.org/XML/1998/namespace'.");
    public static readonly DiagnosticDescriptor XmlnsPrefixDeclaration = Error("XML0017", "Invalid 'xmlns' prefix declaration", "The 'xmlns' prefix cannot be declared.");
    public static readonly DiagnosticDescriptor ReservedNamespace = Error("XML0017", "Reserved namespace", "The namespace '{0}' cannot be bound to '{1}'.");
    public static readonly DiagnosticDescriptor ColonInProcessingInstructionTarget = Error("XML0017", "Colon in processing instruction target", "A processing instruction target cannot contain a colon.");
    public static readonly DiagnosticDescriptor XmlnsElementPrefix = Error("XML0017", "Invalid element prefix", "An element name cannot use the 'xmlns' prefix.");

    public static readonly DiagnosticDescriptor DocumentTypeCase = Error("XML0018", "Invalid document type declaration", "The document type declaration must be written '<!DOCTYPE', not '{0}'.");
    public static readonly DiagnosticDescriptor MissingDocumentTypeName = Error("XML0018", "Missing document type name", "The document type declaration must name the root element.");
    public static readonly DiagnosticDescriptor MissingWhitespaceInDocumentType = Error("XML0018", "Missing whitespace in document type declaration", "'<!DOCTYPE' must be followed by whitespace.");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Error);
}
