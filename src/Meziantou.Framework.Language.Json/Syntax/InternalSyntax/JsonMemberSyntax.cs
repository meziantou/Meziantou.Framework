using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>One member of an object: a name, a colon, and a value.</summary>
/// <remarks>The comma after a member is not part of it: it separates the members of the object's list.</remarks>
internal sealed class JsonMemberSyntax : JsonSyntaxNode
{
    private readonly GreenNode _nameToken;
    private readonly GreenNode _colonToken;
    private readonly GreenNode _value;

    public JsonMemberSyntax(GreenNode nameToken, GreenNode colonToken, GreenNode value)
        : this(nameToken, colonToken, value, diagnostics: null, annotations: null)
    {
    }

    private JsonMemberSyntax(GreenNode nameToken, GreenNode colonToken, GreenNode value, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.JsonMember, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(colonToken);
        _colonToken = colonToken;
        AdjustFlagsAndWidth(value);
        _value = value;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _nameToken,
        1 => _colonToken,
        2 => _value,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new JsonMemberSyntax(slots[0]!, slots[1]!, slots[2]!, GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new JsonMemberSyntax(_nameToken, _colonToken, _value, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new JsonMemberSyntax(_nameToken, _colonToken, _value, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Json.JsonMemberSyntax(this, parent, position);
}
