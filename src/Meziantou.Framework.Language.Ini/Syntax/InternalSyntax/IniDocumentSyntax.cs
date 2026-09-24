using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>A whole INI document: its entries and the end of the text.</summary>
internal sealed class IniDocumentSyntax : IniSyntaxNode
{
    private readonly GreenNode? _entries;
    private readonly GreenNode _endOfFileToken;

    public IniDocumentSyntax(GreenNode? entries, GreenNode endOfFileToken)
        : this(entries, endOfFileToken, diagnostics: null, annotations: null)
    {
    }

    private IniDocumentSyntax(GreenNode? entries, GreenNode endOfFileToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.IniDocument, diagnostics, annotations)
    {
        SlotCount = 2;
        AdjustFlagsAndWidth(entries);
        _entries = entries;
        AdjustFlagsAndWidth(endOfFileToken);
        _endOfFileToken = endOfFileToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _entries,
        1 => _endOfFileToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new IniDocumentSyntax(slots[0], RequiredSlot(slots[1]), GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new IniDocumentSyntax(_entries, _endOfFileToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new IniDocumentSyntax(_entries, _endOfFileToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Ini.IniDocumentSyntax(this, parent, position);
}
