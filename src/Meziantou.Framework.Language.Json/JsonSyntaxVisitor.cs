namespace Meziantou.Framework.Language.Json;

/// <summary>Base visitor for walking JSON syntax trees without returning a value.</summary>
public abstract class JsonSyntaxVisitor
{
    public virtual void Visit(JsonSyntaxNode? node)
    {
        if (node is null)
            return;

        node.Accept(this);
    }

    protected virtual void DefaultVisit(JsonSyntaxNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            Visit(child);
        }
    }

    public virtual void VisitDocument(JsonDocumentSyntax node) => DefaultVisit(node);
    public virtual void VisitObject(JsonObjectSyntax node) => DefaultVisit(node);
    public virtual void VisitMember(JsonMemberSyntax node) => DefaultVisit(node);
    public virtual void VisitArray(JsonArraySyntax node) => DefaultVisit(node);
    public virtual void VisitArrayElement(JsonArrayElementSyntax node) => DefaultVisit(node);
    public virtual void VisitString(JsonStringSyntax node) => DefaultVisit(node);
    public virtual void VisitNumber(JsonNumberSyntax node) => DefaultVisit(node);
    public virtual void VisitLiteral(JsonLiteralSyntax node) => DefaultVisit(node);
    public virtual void VisitSkippedText(JsonSkippedTextSyntax node) => DefaultVisit(node);
}

/// <summary>Base visitor for walking JSON syntax trees and returning a value.</summary>
/// <typeparam name="TResult">Type returned by visit methods.</typeparam>
public abstract class JsonSyntaxVisitor<TResult>
{
    public virtual TResult Visit(JsonSyntaxNode? node)
    {
        if (node is null)
            return default!;

        return node.Accept(this);
    }

    protected virtual TResult DefaultVisit(JsonSyntaxNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            _ = Visit(child);
        }

        return default!;
    }

    public virtual TResult VisitDocument(JsonDocumentSyntax node) => DefaultVisit(node);
    public virtual TResult VisitObject(JsonObjectSyntax node) => DefaultVisit(node);
    public virtual TResult VisitMember(JsonMemberSyntax node) => DefaultVisit(node);
    public virtual TResult VisitArray(JsonArraySyntax node) => DefaultVisit(node);
    public virtual TResult VisitArrayElement(JsonArrayElementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitString(JsonStringSyntax node) => DefaultVisit(node);
    public virtual TResult VisitNumber(JsonNumberSyntax node) => DefaultVisit(node);
    public virtual TResult VisitLiteral(JsonLiteralSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSkippedText(JsonSkippedTextSyntax node) => DefaultVisit(node);
}
