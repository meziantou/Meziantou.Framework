using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A table header such as <c>[database]</c>.</summary>
internal sealed class TomlTableSyntax : TomlEntrySyntax
{
    private readonly GreenNode _openBracketToken;
    private readonly GreenNode _nameToken;
    private readonly GreenNode _closeBracketToken;

    public TomlTableSyntax(GreenNode openBracketToken, GreenNode nameToken, GreenNode closeBracketToken)
        : this(openBracketToken, nameToken, closeBracketToken, diagnostics: null, annotations: null)
    {
    }

    private TomlTableSyntax(GreenNode openBracketToken, GreenNode nameToken, GreenNode closeBracketToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlTable, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(openBracketToken);
        _openBracketToken = openBracketToken;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(closeBracketToken);
        _closeBracketToken = closeBracketToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _openBracketToken,
        1 => _nameToken,
        2 => _closeBracketToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlTableSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlTableSyntax(_openBracketToken, _nameToken, _closeBracketToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlTableSyntax(_openBracketToken, _nameToken, _closeBracketToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlTableSyntax(this, parent, position);
}
