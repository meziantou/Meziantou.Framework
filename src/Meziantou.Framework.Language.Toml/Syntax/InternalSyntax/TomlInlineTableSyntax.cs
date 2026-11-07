using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>An inline table: braces around a comma-separated list of key/value pairs.</summary>
internal sealed class TomlInlineTableSyntax : TomlValueSyntax
{
    private readonly GreenNode _openBraceToken;
    private readonly GreenNode? _properties;
    private readonly GreenNode _closeBraceToken;

    public TomlInlineTableSyntax(GreenNode openBraceToken, GreenNode? properties, GreenNode closeBraceToken)
        : this(openBraceToken, properties, closeBraceToken, diagnostics: null, annotations: null)
    {
    }

    private TomlInlineTableSyntax(GreenNode openBraceToken, GreenNode? properties, GreenNode closeBraceToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlInlineTable, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(openBraceToken);
        _openBraceToken = openBraceToken;
        AdjustFlagsAndWidth(properties);
        _properties = properties;
        AdjustFlagsAndWidth(closeBraceToken);
        _closeBraceToken = closeBraceToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _openBraceToken,
        1 => _properties,
        2 => _closeBraceToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlInlineTableSyntax(RequiredSlot(slots[0]), slots[1], RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override bool IsSeparatedListSlot(int index) => index is 1;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlInlineTableSyntax(_openBraceToken, _properties, _closeBraceToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlInlineTableSyntax(_openBraceToken, _properties, _closeBraceToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlInlineTableSyntax(this, parent, position);
}
