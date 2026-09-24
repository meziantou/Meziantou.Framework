using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>A key/value pair.</summary>
internal sealed class IniPropertySyntax : IniEntrySyntax
{
    private readonly GreenNode _keyToken;
    private readonly GreenNode _separatorToken;
    private readonly GreenNode _valueToken;

    public IniPropertySyntax(GreenNode keyToken, GreenNode separatorToken, GreenNode valueToken)
        : this(keyToken, separatorToken, valueToken, diagnostics: null, annotations: null)
    {
    }

    private IniPropertySyntax(GreenNode keyToken, GreenNode separatorToken, GreenNode valueToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.IniProperty, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(keyToken);
        _keyToken = keyToken;
        AdjustFlagsAndWidth(separatorToken);
        _separatorToken = separatorToken;
        AdjustFlagsAndWidth(valueToken);
        _valueToken = valueToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _keyToken,
        1 => _separatorToken,
        2 => _valueToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new IniPropertySyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new IniPropertySyntax(_keyToken, _separatorToken, _valueToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new IniPropertySyntax(_keyToken, _separatorToken, _valueToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Ini.IniPropertySyntax(this, parent, position);
}
