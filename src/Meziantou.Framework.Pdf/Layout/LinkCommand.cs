using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

/// <summary>A link: an annotation over a rectangle, to a URI or to a heading, whose structure element is <see cref="DrawCommand.Node"/>.</summary>
internal sealed record LinkCommand(StructNode? Node, PdfRectangle Rectangle, Uri? Uri, int HeadingIndex, string Text) : DrawCommand(Node)
{
    public override DrawCommand Translate(double dx, double dy) => this with { Rectangle = FillCommand.Offset(Rectangle, dx, dy) };
}
