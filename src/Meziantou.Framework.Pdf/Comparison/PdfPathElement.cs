namespace Meziantou.Framework.Pdf;

/// <summary>A path a page paints: its bounding box in default user space, how it is painted, and the color operators in effect.</summary>
/// <param name="Bounds">The bounding box of the points of the path (the control points of its curves included), in default user space.</param>
/// <param name="Paint">How the path is painted.</param>
/// <param name="FillColor">The operator that set the fill color, such as <c>1 0 0 rg</c>, or <see langword="null"/> for the initial color.</param>
/// <param name="StrokeColor">The operator that set the stroke color, such as <c>0 G</c>, or <see langword="null"/> for the initial color.</param>
public sealed record PdfPathElement(PdfRectangle Bounds, PdfPathPaint Paint, string? FillColor, string? StrokeColor)
{
    /// <inheritdoc/>
    public override string ToString() => Paint switch
    {
        PdfPathPaint.Fill => $"fill {FillColor ?? "0 g"} {Bounds}",
        PdfPathPaint.Stroke => $"stroke {StrokeColor ?? "0 G"} {Bounds}",
        _ => $"fill {FillColor ?? "0 g"} stroke {StrokeColor ?? "0 G"} {Bounds}",
    };
}
