namespace Meziantou.Framework.Language.Xml;

/// <summary>Dispatches on the kind of an XML node.</summary>
/// <remarks>
/// Visiting a node does not visit its children. Derive from <see cref="XmlSyntaxWalker"/> to walk a whole tree.
/// </remarks>
/// <example>
/// <code>
/// sealed class Counter : XmlSyntaxWalker
/// {
///     public int Count { get; private set; }
///     public override void VisitElement(XmlElementSyntax node) { Count++; base.VisitElement(node); }
/// }
/// </code>
/// </example>
public abstract class XmlSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(XmlSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(XmlSyntaxNode node)
    {
    }

    public virtual void VisitDocument(XmlDocumentSyntax node) => DefaultVisit(node);
    public virtual void VisitElement(XmlElementSyntax node) => DefaultVisit(node);
    public virtual void VisitEmptyElement(XmlEmptyElementSyntax node) => DefaultVisit(node);
    public virtual void VisitElementStartTag(XmlElementStartTagSyntax node) => DefaultVisit(node);
    public virtual void VisitElementEndTag(XmlElementEndTagSyntax node) => DefaultVisit(node);
    public virtual void VisitAttribute(XmlAttributeSyntax node) => DefaultVisit(node);
    public virtual void VisitText(XmlTextSyntax node) => DefaultVisit(node);
    public virtual void VisitComment(XmlCommentSyntax node) => DefaultVisit(node);
    public virtual void VisitCDataSection(XmlCDataSectionSyntax node) => DefaultVisit(node);
    public virtual void VisitDeclaration(XmlDeclarationSyntax node) => DefaultVisit(node);
    public virtual void VisitProcessingInstruction(XmlProcessingInstructionSyntax node) => DefaultVisit(node);
    public virtual void VisitDocumentType(XmlDocumentTypeSyntax node) => DefaultVisit(node);
    public virtual void VisitSkippedText(XmlSkippedTextSyntax node) => DefaultVisit(node);
}

/// <summary>Dispatches on the kind of an XML node and returns a result.</summary>
/// <typeparam name="TResult">What visiting a node produces.</typeparam>
/// <example>
/// <code>
/// sealed class ElementName : XmlSyntaxVisitor&lt;string?&gt;
/// {
///     public override string? VisitElement(XmlElementSyntax node) => node.Name;
/// }
/// </code>
/// </example>
public abstract class XmlSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(XmlSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(XmlSyntaxNode node) => default;

    public virtual TResult? VisitDocument(XmlDocumentSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitElement(XmlElementSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitEmptyElement(XmlEmptyElementSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitElementStartTag(XmlElementStartTagSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitElementEndTag(XmlElementEndTagSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitAttribute(XmlAttributeSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitText(XmlTextSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitComment(XmlCommentSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitCDataSection(XmlCDataSectionSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitDeclaration(XmlDeclarationSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitProcessingInstruction(XmlProcessingInstructionSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitDocumentType(XmlDocumentTypeSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitSkippedText(XmlSkippedTextSyntax node) => DefaultVisit(node);
}
