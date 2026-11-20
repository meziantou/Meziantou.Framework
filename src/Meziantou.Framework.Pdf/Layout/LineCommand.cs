using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

internal sealed record LineCommand(StructNode? Node, double X1, double Y1, double X2, double Y2, double Width, PdfColor Color) : DrawCommand(Node)
{
    public override DrawCommand Translate(double dx, double dy) => this with { X1 = X1 + dx, Y1 = Y1 + dy, X2 = X2 + dx, Y2 = Y2 + dy };
}
