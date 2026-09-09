using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>A whole JSON document.</summary>
/// <remarks>
/// <see cref="Values"/> is a list rather than a single value so that a document with trailing garbage keeps every
/// part of its text. A well-formed document has exactly one.
/// </remarks>
public sealed class JsonDocumentSyntax : JsonSyntaxNode
{
    private SyntaxNode? _values;

    internal JsonDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the values the document holds, which is one for a well-formed document.</summary>
    public SyntaxList<JsonValueSyntax> Values => new(GetRedAtZero(ref _values));

    /// <summary>Gets the root value, or <see langword="null"/> when the document has none.</summary>
    public JsonValueSyntax? Value => Values.Count > 0 ? Values[0] : null;

    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Returns this document with the given parts, or itself when nothing changed.</summary>
    public JsonDocumentSyntax Update(SyntaxList<JsonValueSyntax> values, SyntaxToken endOfFileToken)
    {
        if (values.Green == Green.GetSlot(0) && endOfFileToken.Node == Green.GetSlot(1))
            return this;

        return SyntaxFactory.JsonDocument(values, endOfFileToken).WithAnnotationsFrom(this);
    }

    public JsonDocumentSyntax WithValues(SyntaxList<JsonValueSyntax> values) => Update(values, EndOfFileToken);
    public JsonDocumentSyntax WithValue(JsonValueSyntax? value) => WithValues(value is null ? default : new SyntaxList<JsonValueSyntax>(value));
    public JsonDocumentSyntax WithEndOfFileToken(SyntaxToken endOfFileToken) => Update(Values, endOfFileToken);
    public JsonDocumentSyntax AddValues(params JsonValueSyntax[] items) => WithValues(Values.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _values) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _values : null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonDocument(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonDocument(this);
    }
}
