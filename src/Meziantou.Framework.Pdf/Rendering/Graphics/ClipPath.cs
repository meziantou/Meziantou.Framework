using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>A clipping path in device space, and the paths that clipped before it (ISO 32000-2, 8.5.4): the clipping region is their intersection.</summary>
internal sealed record ClipPath(VectorPath Path, FillRule FillRule, ClipPath? Previous)
{
    public int Depth { get; } = (Previous?.Depth ?? 0) + 1;
}
