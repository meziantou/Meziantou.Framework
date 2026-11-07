using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>Text the parser could not use, kept so the document still round-trips.</summary>
internal sealed class IniSkippedTextSyntax : IniEntrySyntax
{
    private readonly GreenNode? _tokens;

    public IniSkippedTextSyntax(GreenNode? tokens)
        : this(tokens, diagnostics: null, annotations: null)
    {
    }

    private IniSkippedTextSyntax(GreenNode? tokens, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.IniSkippedText, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(tokens);
        _tokens = tokens;
        SetFlags(NodeFlags.ContainsSkippedText);
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _tokens : null;
    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new IniSkippedTextSyntax(slots[0], GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new IniSkippedTextSyntax(_tokens, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new IniSkippedTextSyntax(_tokens, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Ini.IniSkippedTextSyntax(this, parent, position);
}
