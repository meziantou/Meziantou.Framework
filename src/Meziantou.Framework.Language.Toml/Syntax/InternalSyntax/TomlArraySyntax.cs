using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

internal sealed class TomlArraySyntax : TomlSyntaxNode
{
    private readonly GreenNode _openBracket;
    private readonly GreenNode? _contents;
    private readonly GreenNode _closeBracket;

    public TomlArraySyntax(GreenNode openBracket, GreenNode? contents, GreenNode closeBracket)
        : this(openBracket, contents, closeBracket, diagnostics: null, annotations: null)
    {
    }

    private TomlArraySyntax(GreenNode openBracket, GreenNode? contents, GreenNode closeBracket, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlArray, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(openBracket);
        _openBracket = openBracket;
        AdjustFlagsAndWidth(contents);
        _contents = contents;
        AdjustFlagsAndWidth(closeBracket);
        _closeBracket = closeBracket;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _openBracket,
        1 => _contents,
        2 => _closeBracket,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots)
        => new TomlArraySyntax(RequiredSlot(slots[0]), slots[1], RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());

    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics)
        => new TomlArraySyntax(_openBracket, _contents, _closeBracket, diagnostics, GetAnnotations());

    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations)
        => new TomlArraySyntax(_openBracket, _contents, _closeBracket, GetDiagnostics(), annotations);

    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position)
        => new Toml.TomlArraySyntax(this, parent, position);
}
