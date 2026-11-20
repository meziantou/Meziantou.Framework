using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

/// <summary>What a flow document draws, in the coordinates of a page (or of a cell before it is placed).</summary>
internal abstract record DrawCommand(StructNode? Node)
{
    public abstract DrawCommand Translate(double dx, double dy);
}
