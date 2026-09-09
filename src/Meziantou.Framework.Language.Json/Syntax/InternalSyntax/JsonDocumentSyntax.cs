using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>A whole JSON document: its values and the end of the text.</summary>
/// <remarks>
/// There is a list of values rather than one, because a document with trailing garbage has to keep every part of its
/// text and still round-trip.
/// </remarks>
internal sealed class JsonDocumentSyntax : JsonSyntaxNode
{
    private readonly GreenNode? _values;
    private readonly GreenNode _endOfFileToken;

    public JsonDocumentSyntax(GreenNode? values, GreenNode endOfFileToken)
        : this(values, endOfFileToken, diagnostics: null, annotations: null)
    {
    }

    private JsonDocumentSyntax(GreenNode? values, GreenNode endOfFileToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.JsonDocument, diagnostics, annotations)
    {
        SlotCount = 2;
        AdjustFlagsAndWidth(values);
        _values = values;
        AdjustFlagsAndWidth(endOfFileToken);
        _endOfFileToken = endOfFileToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _values,
        1 => _endOfFileToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonDocumentSyntax(slots[0], slots[1]!, GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonDocumentSyntax(_values, _endOfFileToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonDocumentSyntax(_values, _endOfFileToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonDocumentSyntax(this, parent, position);
}
