using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>A number value.</summary>
internal sealed class JsonNumberSyntax : JsonValueSyntax
{
    private readonly GreenNode _numberToken;

    public JsonNumberSyntax(GreenNode numberToken)
        : this(numberToken, diagnostics: null, annotations: null)
    {
    }

    private JsonNumberSyntax(GreenNode numberToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.JsonNumber, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(numberToken);
        _numberToken = numberToken;
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _numberToken : null;

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonNumberSyntax(slots[0]!, GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonNumberSyntax(_numberToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonNumberSyntax(_numberToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonNumberSyntax(this, parent, position);
}
