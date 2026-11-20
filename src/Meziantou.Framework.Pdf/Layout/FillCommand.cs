using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

internal sealed record FillCommand(StructNode? Node, PdfRectangle Rectangle, PdfColor Color) : DrawCommand(Node)
{
    public override DrawCommand Translate(double dx, double dy) => this with { Rectangle = Offset(Rectangle, dx, dy) };

    internal static PdfRectangle Offset(PdfRectangle rectangle, double dx, double dy) => new(rectangle.Left + dx, rectangle.Bottom + dy, rectangle.Right + dx, rectangle.Top + dy);
}
