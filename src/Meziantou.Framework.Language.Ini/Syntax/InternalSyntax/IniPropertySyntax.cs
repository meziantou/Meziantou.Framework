using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>A key/value pair, with the lines its value continues on.</summary>
internal sealed class IniPropertySyntax : IniEntrySyntax
{
    private readonly GreenNode _keyToken;
    private readonly GreenNode _separatorToken;
    private readonly GreenNode _valueToken;
    private readonly GreenNode? _continuationTokens;

    public IniPropertySyntax(GreenNode keyToken, GreenNode separatorToken, GreenNode valueToken, GreenNode? continuationTokens)
        : this(keyToken, separatorToken, valueToken, continuationTokens, diagnostics: null, annotations: null)
    {
    }

    private IniPropertySyntax(GreenNode keyToken, GreenNode separatorToken, GreenNode valueToken, GreenNode? continuationTokens, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.IniProperty, diagnostics, annotations)
    {
        SlotCount = 4;
        AdjustFlagsAndWidth(keyToken);
        _keyToken = keyToken;
        AdjustFlagsAndWidth(separatorToken);
        _separatorToken = separatorToken;
        AdjustFlagsAndWidth(valueToken);
        _valueToken = valueToken;
        AdjustFlagsAndWidth(continuationTokens);
        _continuationTokens = continuationTokens;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _keyToken,
        1 => _separatorToken,
        2 => _valueToken,
        3 => _continuationTokens,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new IniPropertySyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), slots[3], GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 3;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new IniPropertySyntax(_keyToken, _separatorToken, _valueToken, _continuationTokens, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new IniPropertySyntax(_keyToken, _separatorToken, _valueToken, _continuationTokens, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Ini.IniPropertySyntax(this, parent, position);
}
