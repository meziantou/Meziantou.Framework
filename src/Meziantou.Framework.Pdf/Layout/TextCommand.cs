using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

internal sealed record TextCommand(StructNode? Node, string Text, PdfFont Font, double Size, PdfColor Color, double X, double Y) : DrawCommand(Node)
{
    public override DrawCommand Translate(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
}
