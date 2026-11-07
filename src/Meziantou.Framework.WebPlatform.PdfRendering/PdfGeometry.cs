using Meziantou.Framework.Pdf;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

/// <summary>The conversions between the geometry of the display lists (CSS px, y down) and the geometry of PDF (points, y up).</summary>
internal static class PdfGeometry
{
    /// <summary>The number of PDF points per CSS pixel (72 points and 96 CSS pixels per inch).</summary>
    public const double PointsPerPixel = 72d / 96;

    /// <summary>The transform from the CSS pixels of a page (y down) to its default user space (points, y up).</summary>
    public static DisplayMatrix GetPageMatrix(double pageHeight) => new(PointsPerPixel, 0, 0, -PointsPerPixel, 0, pageHeight * PointsPerPixel);

    public static PdfMatrix ToPdf(DisplayMatrix matrix) => new(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);

    /// <summary>Whether a 2D transform can be inverted (and its components are finite).</summary>
    public static bool IsInvertible(DisplayMatrix matrix)
    {
        var determinant = (matrix.A * matrix.D) - (matrix.B * matrix.C);
        return double.IsFinite(determinant) && determinant != 0 && double.IsFinite(matrix.E) && double.IsFinite(matrix.F);
    }

    /// <summary>Returns the inverse of a 2D transform, or null when it cannot be inverted.</summary>
    public static DisplayMatrix? Invert(DisplayMatrix matrix)
    {
        if (!IsInvertible(matrix))
            return null;

        var determinant = (matrix.A * matrix.D) - (matrix.B * matrix.C);
        var a = matrix.D / determinant;
        var b = -matrix.B / determinant;
        var c = -matrix.C / determinant;
        var d = matrix.A / determinant;
        var e = -((a * matrix.E) + (c * matrix.F));
        var f = -((b * matrix.E) + (d * matrix.F));
        return new DisplayMatrix(a, b, c, d, e, f);
    }

    /// <summary>Returns the bounds of a rectangle transformed by a 2D transform (the 2D part of a 3D transform).</summary>
    public static PhysicalRect Transform(DisplayMatrix matrix, PhysicalRect rect)
    {
        var affine = matrix with { Matrix3d = null };
        var p1 = affine.Apply(new PhysicalOffset(rect.Left, rect.Top));
        var p2 = affine.Apply(new PhysicalOffset(rect.Right, rect.Top));
        var p3 = affine.Apply(new PhysicalOffset(rect.Right, rect.Bottom));
        var p4 = affine.Apply(new PhysicalOffset(rect.Left, rect.Bottom));
        var left = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var top = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var right = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var bottom = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new PhysicalRect(left, top, right - left, bottom - top);
    }

    /// <summary>Returns the intersection of two rectangles (empty when they do not intersect).</summary>
    public static PhysicalRect Intersect(PhysicalRect first, PhysicalRect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right > left && bottom > top ? new PhysicalRect(left, top, right - left, bottom - top) : PhysicalRect.Empty;
    }

    public static PhysicalRect Inflate(PhysicalRect rect, double margin) => new(rect.X - margin, rect.Y - margin, rect.Width + (2 * margin), rect.Height + (2 * margin));

    /// <summary>Returns a rectangle of the CSS pixels of a page in the default user space of its PDF page.</summary>
    public static PdfRectangle ToPdfRectangle(PhysicalRect rect, double pageHeight)
        => new(rect.Left * PointsPerPixel, (pageHeight - rect.Bottom) * PointsPerPixel, rect.Right * PointsPerPixel, (pageHeight - rect.Top) * PointsPerPixel);

    /// <summary>
    /// Returns the bounds of what a drawing item paints, in its coordinates, or null when it paints the whole clip
    /// (<see cref="DrawPaintItem"/>) or nothing. The bounds of a glyph run are estimated from the font size.
    /// </summary>
    public static PhysicalRect? GetBounds(DisplayItem item)
    {
        PhysicalRect? bounds = item switch
        {
            DrawRectItem draw => Stroke(draw.Rect, draw.Paint),
            DrawRoundedRectItem draw => Stroke(draw.Rect.Rect, draw.Paint),
            DrawRingItem draw => draw.Outer.Rect,
            DrawPathItem draw => Stroke(draw.Path.GetBounds(), draw.Paint),
            DrawLineItem draw => Stroke(new PhysicalRect(Math.Min(draw.Start.X, draw.End.X), Math.Min(draw.Start.Y, draw.End.Y), Math.Abs(draw.End.X - draw.Start.X), Math.Abs(draw.End.Y - draw.Start.Y)), draw.Paint, minimum: 1),
            DrawGlyphsItem draw => GetGlyphBounds(draw.Run),
            DrawImageItem draw => draw.DestinationRect,
            _ => null,
        };

        if (bounds is { } value && GetPaint(item) is { BlurSigma: > 0 and var sigma } && double.IsFinite(sigma))
            return Inflate(value, 3 * sigma);

        return bounds;

        static PhysicalRect Stroke(PhysicalRect rect, DisplayPaint paint, double minimum = 0)
        {
            if (paint.Style != PaintStyle.Stroke && minimum == 0)
                return rect;

            // The miter joins can extend beyond half of the width
            var width = paint.Stroke?.Width ?? 1;
            var margin = Math.Max(minimum, width * Math.Max(1, paint.Stroke?.MiterLimit ?? 4) / 2);
            return Inflate(rect, double.IsFinite(margin) ? margin : 0);
        }
    }

    /// <summary>Returns the paint of a drawing item, or null.</summary>
    public static DisplayPaint? GetPaint(DisplayItem item) => item switch
    {
        DrawPaintItem draw => draw.Paint,
        DrawRectItem draw => draw.Paint,
        DrawRoundedRectItem draw => draw.Paint,
        DrawRingItem draw => draw.Paint,
        DrawPathItem draw => draw.Paint,
        DrawLineItem draw => draw.Paint,
        DrawGlyphsItem draw => draw.Paint,
        DrawImageItem draw => draw.Paint,
        _ => null,
    };

    /// <summary>Estimates the bounds of the glyphs of a run: from the ascent to the descent of the font, and to the end of the advance of the last glyph.</summary>
    public static PhysicalRect? GetGlyphBounds(GlyphRun run)
    {
        var count = Math.Min(run.Glyphs.Length, run.Positions.Length);
        if (count == 0)
            return null;

        var size = run.Font.Size;
        var (ascent, descent, advance) = (0.9 * size, 0.3 * size, size);
        if (run.Font.File.Faces is { } faces && run.Font.FaceIndex < faces.Count)
        {
            var face = faces[run.Font.FaceIndex].Face;
            var scale = size / face.UnitsPerEm;
            ascent = Math.Max(face.HorizontalAscender, face.YMax) * scale;
            descent = Math.Max(-face.HorizontalDescender, -face.YMin) * scale;
            var last = run.Glyphs[count - 1];
            advance = last < face.GlyphCount ? face.GetAdvanceWidth(last) * scale * run.ScaleX : size;
        }

        var left = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.PositiveInfinity;
        var bottom = double.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            var position = run.Positions[i];
            left = Math.Min(left, position.X);
            right = Math.Max(right, position.X);
            top = Math.Min(top, position.Y - ascent);
            bottom = Math.Max(bottom, position.Y + descent);
        }

        right += Math.Max(0, advance);
        if (!double.IsFinite(left) || !double.IsFinite(right) || !double.IsFinite(top) || !double.IsFinite(bottom))
            return null;

        return new PhysicalRect(left, top, right - left, bottom - top);
    }
}
