using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>A section header such as <c>[database]</c>.</summary>
internal sealed class IniSectionSyntax : IniEntrySyntax
{
    private readonly GreenNode _openBracketToken;
    private readonly GreenNode _nameToken;
    private readonly GreenNode _closeBracketToken;

    public IniSectionSyntax(GreenNode openBracketToken, GreenNode nameToken, GreenNode closeBracketToken)
        : this(openBracketToken, nameToken, closeBracketToken, diagnostics: null, annotations: null)
    {
    }

    private IniSectionSyntax(GreenNode openBracketToken, GreenNode nameToken, GreenNode closeBracketToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.IniSection, diagnostics, annotations)
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

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new IniSectionSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new IniSectionSyntax(_openBracketToken, _nameToken, _closeBracketToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new IniSectionSyntax(_openBracketToken, _nameToken, _closeBracketToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Ini.IniSectionSyntax(this, parent, position);
}
