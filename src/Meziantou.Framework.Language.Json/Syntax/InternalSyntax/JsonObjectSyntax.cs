using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>An object: braces around a comma-separated list of members.</summary>
internal sealed class JsonObjectSyntax : JsonValueSyntax
{
    private readonly GreenNode _openBraceToken;
    private readonly GreenNode? _members;
    private readonly GreenNode _closeBraceToken;

    public JsonObjectSyntax(GreenNode openBraceToken, GreenNode? members, GreenNode closeBraceToken)
        : this(openBraceToken, members, closeBraceToken, diagnostics: null, annotations: null)
    {
    }

    private JsonObjectSyntax(GreenNode openBraceToken, GreenNode? members, GreenNode closeBraceToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.JsonObject, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(openBraceToken);
        _openBraceToken = openBraceToken;
        AdjustFlagsAndWidth(members);
        _members = members;
        AdjustFlagsAndWidth(closeBraceToken);
        _closeBraceToken = closeBraceToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _openBraceToken,
        1 => _members,
        2 => _closeBraceToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonObjectSyntax(slots[0]!, slots[1], slots[2]!, GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonObjectSyntax(_openBraceToken, _members, _closeBraceToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonObjectSyntax(_openBraceToken, _members, _closeBraceToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonObjectSyntax(this, parent, position);
}
