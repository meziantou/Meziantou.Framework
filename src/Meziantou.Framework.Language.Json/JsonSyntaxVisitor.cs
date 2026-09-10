namespace Meziantou.Framework.Language.Json;

/// <summary>Dispatches on the kind of a JSON node.</summary>
/// <remarks>
/// Visiting a node does not visit its children. Derive from <see cref="JsonSyntaxWalker"/> to walk a whole tree.
/// </remarks>
public abstract class JsonSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(JsonSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(JsonSyntaxNode node)
    {
    }

    public virtual void VisitJsonDocument(JsonDocumentSyntax node) => DefaultVisit(node);
    public virtual void VisitJsonObject(JsonObjectSyntax node) => DefaultVisit(node);
    public virtual void VisitJsonMember(JsonMemberSyntax node) => DefaultVisit(node);
    public virtual void VisitJsonArray(JsonArraySyntax node) => DefaultVisit(node);
    public virtual void VisitJsonString(JsonStringSyntax node) => DefaultVisit(node);
    public virtual void VisitJsonNumber(JsonNumberSyntax node) => DefaultVisit(node);
    public virtual void VisitJsonLiteral(JsonLiteralSyntax node) => DefaultVisit(node);
    public virtual void VisitJsonSkippedText(JsonSkippedTextSyntax node) => DefaultVisit(node);
}

/// <summary>Dispatches on the kind of a JSON node and returns a result.</summary>
/// <typeparam name="TResult">What visiting a node produces.</typeparam>
public abstract class JsonSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(JsonSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(JsonSyntaxNode node) => default;

    public virtual TResult? VisitJsonDocument(JsonDocumentSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitJsonObject(JsonObjectSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitJsonMember(JsonMemberSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitJsonArray(JsonArraySyntax node) => DefaultVisit(node);
    public virtual TResult? VisitJsonString(JsonStringSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitJsonNumber(JsonNumberSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitJsonLiteral(JsonLiteralSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitJsonSkippedText(JsonSkippedTextSyntax node) => DefaultVisit(node);
}
