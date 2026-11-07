using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A key/value pair.</summary>
internal sealed class TomlPropertySyntax : TomlEntrySyntax
{
    private readonly GreenNode _key;
    private readonly GreenNode _equalsToken;
    private readonly GreenNode _value;

    public TomlPropertySyntax(GreenNode key, GreenNode equalsToken, GreenNode value)
        : this(key, equalsToken, value, diagnostics: null, annotations: null)
    {
    }

    private TomlPropertySyntax(GreenNode key, GreenNode equalsToken, GreenNode value, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlProperty, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(key);
        _key = key;
        AdjustFlagsAndWidth(equalsToken);
        _equalsToken = equalsToken;
        AdjustFlagsAndWidth(value);
        _value = value;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _key,
        1 => _equalsToken,
        2 => _value,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlPropertySyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlPropertySyntax(_key, _equalsToken, _value, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlPropertySyntax(_key, _equalsToken, _value, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlPropertySyntax(this, parent, position);
}
