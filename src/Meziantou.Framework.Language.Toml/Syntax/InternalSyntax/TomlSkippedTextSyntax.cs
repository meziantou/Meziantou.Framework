using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Text the parser could not use, kept so the document still round-trips.</summary>
internal sealed class TomlSkippedTextSyntax : TomlEntrySyntax
{
    private readonly GreenNode? _tokens;

    public TomlSkippedTextSyntax(GreenNode? tokens)
        : this(tokens, diagnostics: null, annotations: null)
    {
    }

    private TomlSkippedTextSyntax(GreenNode? tokens, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlSkippedText, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(tokens);
        _tokens = tokens;
        SetFlags(NodeFlags.ContainsSkippedText);
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _tokens : null;
    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlSkippedTextSyntax(slots[0], GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlSkippedTextSyntax(_tokens, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlSkippedTextSyntax(_tokens, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlSkippedTextSyntax(this, parent, position);
}
