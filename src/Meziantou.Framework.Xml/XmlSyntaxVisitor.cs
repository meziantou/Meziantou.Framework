namespace Meziantou.Framework.Xml;

/// <summary>Base visitor for walking XML syntax trees without returning a value.</summary>
/// <example>
/// <code>
/// sealed class Counter : XmlSyntaxVisitor
/// {
///     public int Count { get; private set; }
///     public override void VisitElement(XmlElementSyntax node) => Count++;
/// }
/// </code>
/// </example>
public abstract class XmlSyntaxVisitor
{
    public virtual void Visit(XmlSyntaxNode? node)
    {
        if (node is null)
            return;

        node.Accept(this);
    }

    protected virtual void DefaultVisit(XmlSyntaxNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            Visit(child);
        }
    }

    public virtual void VisitDocument(XmlDocumentSyntax node) => DefaultVisit(node);
    public virtual void VisitElement(XmlElementSyntax node) => DefaultVisit(node);
    public virtual void VisitEndTag(XmlEndTagSyntax node) => DefaultVisit(node);
    public virtual void VisitAttribute(XmlAttributeSyntax node) => DefaultVisit(node);
    public virtual void VisitText(XmlTextSyntax node) => DefaultVisit(node);
    public virtual void VisitComment(XmlCommentSyntax node) => DefaultVisit(node);
    public virtual void VisitCDataSection(XmlCDataSectionSyntax node) => DefaultVisit(node);
    public virtual void VisitDeclaration(XmlDeclarationSyntax node) => DefaultVisit(node);
    public virtual void VisitProcessingInstruction(XmlProcessingInstructionSyntax node) => DefaultVisit(node);
    public virtual void VisitDocumentType(XmlDocumentTypeSyntax node) => DefaultVisit(node);
    public virtual void VisitSkippedText(XmlSkippedTextSyntax node) => DefaultVisit(node);
}

/// <summary>Base visitor for walking XML syntax trees and returning a value.</summary>
/// <typeparam name="TResult">Type returned by visit methods.</typeparam>
/// <example>
/// <code>
/// sealed class ElementNameVisitor : XmlSyntaxVisitor&lt;string?&gt;
/// {
///     public override string? VisitElement(XmlElementSyntax node) => node.Name;
/// }
/// </code>
/// </example>
public abstract class XmlSyntaxVisitor<TResult>
{
    public virtual TResult Visit(XmlSyntaxNode? node)
    {
        if (node is null)
            return default!;

        return node.Accept(this);
    }

    protected virtual TResult DefaultVisit(XmlSyntaxNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            _ = Visit(child);
        }

        return default!;
    }

    public virtual TResult VisitDocument(XmlDocumentSyntax node) => DefaultVisit(node);
    public virtual TResult VisitElement(XmlElementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitEndTag(XmlEndTagSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAttribute(XmlAttributeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitText(XmlTextSyntax node) => DefaultVisit(node);
    public virtual TResult VisitComment(XmlCommentSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCDataSection(XmlCDataSectionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitDeclaration(XmlDeclarationSyntax node) => DefaultVisit(node);
    public virtual TResult VisitProcessingInstruction(XmlProcessingInstructionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitDocumentType(XmlDocumentTypeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSkippedText(XmlSkippedTextSyntax node) => DefaultVisit(node);
}
