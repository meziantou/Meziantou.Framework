using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>A string value.</summary>
internal sealed class JsonStringSyntax : JsonValueSyntax
{
    private readonly GreenNode _stringToken;

    public JsonStringSyntax(GreenNode stringToken)
        : this(stringToken, diagnostics: null, annotations: null)
    {
    }

    private JsonStringSyntax(GreenNode stringToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.JsonString, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(stringToken);
        _stringToken = stringToken;
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _stringToken : null;

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonStringSyntax(slots[0]!, GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonStringSyntax(_stringToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonStringSyntax(_stringToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonStringSyntax(this, parent, position);
}
