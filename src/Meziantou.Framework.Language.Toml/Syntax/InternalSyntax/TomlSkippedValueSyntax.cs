using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A value the parser could not read, or one that is missing, kept so the document still round-trips.</summary>
internal sealed class TomlSkippedValueSyntax : TomlValueSyntax
{
    private readonly GreenNode? _tokens;

    public TomlSkippedValueSyntax(GreenNode? tokens)
        : this(tokens, diagnostics: null, annotations: null)
    {
    }

    private TomlSkippedValueSyntax(GreenNode? tokens, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlSkippedValue, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(tokens);
        _tokens = tokens;
        SetFlags(NodeFlags.ContainsSkippedText);
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _tokens : null;
    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlSkippedValueSyntax(slots[0], GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlSkippedValueSyntax(_tokens, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlSkippedValueSyntax(_tokens, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlSkippedValueSyntax(this, parent, position);
}
