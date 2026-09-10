using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>Text the parser could not use, kept so the document still round-trips.</summary>
/// <remarks>
/// It stands where a value was expected, which is why it is a value: a member whose value is nonsense still needs
/// something in that slot.
/// </remarks>
internal sealed class JsonSkippedTextSyntax : JsonValueSyntax
{
    private readonly GreenNode? _tokens;

    public JsonSkippedTextSyntax(GreenNode? tokens)
        : this(tokens, diagnostics: null, annotations: null)
    {
    }

    private JsonSkippedTextSyntax(GreenNode? tokens, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.JsonSkippedText, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(tokens);
        _tokens = tokens;
        SetFlags(NodeFlags.ContainsSkippedText);
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _tokens : null;

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonSkippedTextSyntax(slots[0], GetDiagnostics(), GetAnnotations());

    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonSkippedTextSyntax(_tokens, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonSkippedTextSyntax(_tokens, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonSkippedTextSyntax(this, parent, position);
}
