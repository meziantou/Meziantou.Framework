using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A table header such as <c>[database]</c>, or an array-of-tables header such as <c>[[products]]</c>.</summary>
/// <remarks>Which of the two it is is the node's kind, so both share one node type.</remarks>
internal sealed class TomlTableSyntax : TomlEntrySyntax
{
    private readonly GreenNode _openBracketToken;
    private readonly GreenNode _key;
    private readonly GreenNode _closeBracketToken;

    public TomlTableSyntax(SyntaxKind kind, GreenNode openBracketToken, GreenNode key, GreenNode closeBracketToken)
        : this(kind, openBracketToken, key, closeBracketToken, diagnostics: null, annotations: null)
    {
    }

    private TomlTableSyntax(SyntaxKind kind, GreenNode openBracketToken, GreenNode key, GreenNode closeBracketToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(openBracketToken);
        _openBracketToken = openBracketToken;
        AdjustFlagsAndWidth(key);
        _key = key;
        AdjustFlagsAndWidth(closeBracketToken);
        _closeBracketToken = closeBracketToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _openBracketToken,
        1 => _key,
        2 => _closeBracketToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlTableSyntax(Kind, RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlTableSyntax(Kind, _openBracketToken, _key, _closeBracketToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlTableSyntax(Kind, _openBracketToken, _key, _closeBracketToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlTableSyntax(this, parent, position);
}
