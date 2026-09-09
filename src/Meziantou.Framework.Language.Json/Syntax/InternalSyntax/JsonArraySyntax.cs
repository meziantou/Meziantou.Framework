using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>An array: brackets around a comma-separated list of values.</summary>
internal sealed class JsonArraySyntax : JsonValueSyntax
{
    private readonly GreenNode _openBracketToken;
    private readonly GreenNode? _elements;
    private readonly GreenNode _closeBracketToken;

    public JsonArraySyntax(GreenNode openBracketToken, GreenNode? elements, GreenNode closeBracketToken)
        : this(openBracketToken, elements, closeBracketToken, diagnostics: null, annotations: null)
    {
    }

    private JsonArraySyntax(GreenNode openBracketToken, GreenNode? elements, GreenNode closeBracketToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.JsonArray, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(openBracketToken);
        _openBracketToken = openBracketToken;
        AdjustFlagsAndWidth(elements);
        _elements = elements;
        AdjustFlagsAndWidth(closeBracketToken);
        _closeBracketToken = closeBracketToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _openBracketToken,
        1 => _elements,
        2 => _closeBracketToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonArraySyntax(slots[0]!, slots[1], slots[2]!, GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonArraySyntax(_openBracketToken, _elements, _closeBracketToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonArraySyntax(_openBracketToken, _elements, _closeBracketToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonArraySyntax(this, parent, position);
}
