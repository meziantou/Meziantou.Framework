using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A key/value pair.</summary>
internal sealed class TomlPropertySyntax : TomlEntrySyntax
{
    private readonly GreenNode _keyToken;
    private readonly GreenNode _separatorToken;
    private readonly GreenNode _value;

    public TomlPropertySyntax(GreenNode keyToken, GreenNode separatorToken, GreenNode valueToken)
        : this(keyToken, separatorToken, valueToken, diagnostics: null, annotations: null)
    {
    }

    private TomlPropertySyntax(GreenNode keyToken, GreenNode separatorToken, GreenNode valueToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlProperty, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(keyToken);
        _keyToken = keyToken;
        AdjustFlagsAndWidth(separatorToken);
        _separatorToken = separatorToken;
        AdjustFlagsAndWidth(valueToken);
        _value = valueToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _keyToken,
        1 => _separatorToken,
        2 => _value,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlPropertySyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlPropertySyntax(_keyToken, _separatorToken, _value, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlPropertySyntax(_keyToken, _separatorToken, _value, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlPropertySyntax(this, parent, position);
}
