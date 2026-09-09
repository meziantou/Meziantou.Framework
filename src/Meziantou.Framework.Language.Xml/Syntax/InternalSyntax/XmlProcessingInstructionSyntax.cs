using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>A processing instruction: a target and the data it carries.</summary>
internal sealed class XmlProcessingInstructionSyntax : XmlNodeSyntax
{
    private readonly GreenNode _startProcessingInstructionToken;
    private readonly GreenNode _nameToken;
    private readonly GreenNode _dataToken;
    private readonly GreenNode _endProcessingInstructionToken;

    public XmlProcessingInstructionSyntax(GreenNode startProcessingInstructionToken, GreenNode nameToken, GreenNode dataToken, GreenNode endProcessingInstructionToken)
        : this(startProcessingInstructionToken, nameToken, dataToken, endProcessingInstructionToken, diagnostics: null, annotations: null)
    {
    }

    private XmlProcessingInstructionSyntax(GreenNode startProcessingInstructionToken, GreenNode nameToken, GreenNode dataToken, GreenNode endProcessingInstructionToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlProcessingInstruction, diagnostics, annotations)
    {
        SlotCount = 4;
        AdjustFlagsAndWidth(startProcessingInstructionToken);
        _startProcessingInstructionToken = startProcessingInstructionToken;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(dataToken);
        _dataToken = dataToken;
        AdjustFlagsAndWidth(endProcessingInstructionToken);
        _endProcessingInstructionToken = endProcessingInstructionToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _startProcessingInstructionToken,
        1 => _nameToken,
        2 => _dataToken,
        3 => _endProcessingInstructionToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlProcessingInstructionSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), RequiredSlot(slots[3]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlProcessingInstructionSyntax(_startProcessingInstructionToken, _nameToken, _dataToken, _endProcessingInstructionToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlProcessingInstructionSyntax(_startProcessingInstructionToken, _nameToken, _dataToken, _endProcessingInstructionToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlProcessingInstructionSyntax(this, parent, position);
}
