using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

internal sealed record ImageCommand(StructNode? Node, PdfImage Image, PdfRectangle Rectangle) : DrawCommand(Node)
{
    public override DrawCommand Translate(double dx, double dy) => this with { Rectangle = FillCommand.Offset(Rectangle, dx, dy) };
}
