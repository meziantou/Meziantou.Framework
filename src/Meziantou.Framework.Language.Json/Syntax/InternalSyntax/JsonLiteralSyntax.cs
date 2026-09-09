using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>One of <c>true</c>, <c>false</c>, or <c>null</c>.</summary>
/// <remarks>The kind comes from the keyword, so all three share one node type.</remarks>
internal sealed class JsonLiteralSyntax : JsonValueSyntax
{
    private readonly GreenNode _literalToken;

    public JsonLiteralSyntax(SyntaxKind kind, GreenNode literalToken)
        : this(kind, literalToken, diagnostics: null, annotations: null)
    {
    }

    private JsonLiteralSyntax(SyntaxKind kind, GreenNode literalToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(literalToken);
        _literalToken = literalToken;
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _literalToken : null;

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonLiteralSyntax(Kind, RequiredSlot(slots[0]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonLiteralSyntax(Kind, _literalToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonLiteralSyntax(Kind, _literalToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonLiteralSyntax(this, parent, position);
}
