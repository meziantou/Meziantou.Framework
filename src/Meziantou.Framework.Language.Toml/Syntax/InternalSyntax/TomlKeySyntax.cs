using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A key: its parts, and the dots between them when it is a dotted key.</summary>
internal sealed class TomlKeySyntax : TomlSyntaxNode
{
    private readonly GreenNode? _tokens;

    public TomlKeySyntax(GreenNode? tokens)
        : this(tokens, diagnostics: null, annotations: null)
    {
    }

    private TomlKeySyntax(GreenNode? tokens, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlKey, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(tokens);
        _tokens = tokens;
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _tokens : null;
    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlKeySyntax(slots[0], GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlKeySyntax(_tokens, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlKeySyntax(_tokens, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlKeySyntax(this, parent, position);
}
