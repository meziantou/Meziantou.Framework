using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The glyphs a font draws with its own colors.</content>
internal sealed partial class DrawingSession
{
    private ColorSurface? _colorSurface;
    private GradientEvaluator? _foregroundGradient;
    private PooledBuffer? _maskRow;

    /// <summary>
    /// Draws the color glyphs of a layout, or prepares their drawing: each one is drawn on a surface of its own, by the
    /// operations of its display list, then composited onto the frames.
    /// </summary>
    /// <param name="brush">The brush of the call: the color of the text for the glyphs of the spans that have no brush.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="originX">The left of the layout, when it is drawn at a point.</param>
    /// <param name="originY">The top of the layout.</param>
    /// <param name="measure">The path the layout follows, or <see langword="null"/> when it is drawn at a point.</param>
    /// <param name="options">How the layout follows the path.</param>
    /// <param name="paint">
    /// <see langword="false"/> to only rent what the drawing needs: the surface of the largest glyph, and the working
    /// storage of the sweep of every outline. No pixel is changed, and a drawing that follows allocates nothing.
    /// </param>
    private void DrawColorGlyphs(Brush brush, TextLayout layout, double originX, double originY, PathMeasure? measure, TextPathOptions? options, bool paint)
    {
        if (!layout.HasColorGlyphs)
            return;

        var surface = _colorSurface ??= new ColorSurface(_image.Owner.Scope);
        for (var run = 0; run < layout.RunCount; run++)
        {
            layout.GetRun(run, out var face, out var scale, out var start, out var count, out var runBrush, out var isSideways, out var widthScale);
            var mode = (layout.GetBrush(runBrush) ?? brush).CompositeMode;
            var blend = (layout.GetBrush(runBrush) ?? brush).BlendMode;
            var isForegroundSet = false;
            for (var i = start; i < start + count; i++)
            {
                if (layout.GetColorGlyph(i) is not { } glyph)
                    continue;

                Affine2D placement;
                if (measure is null)
                {
                    placement = layout.GetGlyphTransform(i, scale, isSideways, widthScale, originX, originY);
                }
                else if (!layout.TryGetGlyphTransformOnPath(i, scale, isSideways, widthScale, measure, options!, out placement))
                {
                    continue;
                }

                if (_isProjective)
                {
                    // Drawn on an image of the plane of the drawing, then in perspective: only when it is painted
                    if (paint && !glyph.IsEmpty)
                    {
                        DrawProjectedColorGlyph(layout.GetBrush(runBrush) ?? brush, glyph, face, placement, mode, blend);
                    }

                    continue;
                }

                // A color glyph is drawn in the pixels of the canvas: its placement includes the transformation
                placement = ToCanvas(placement);
                if (glyph.IsMask)
                {
                    DrawMaskGlyph(layout.GetBrush(runBrush) ?? brush, glyph, placement, mode, blend, paint);
                    continue;
                }

                if (!surface.Reserve(glyph, placement, Width, Height))
                    continue;

                if (!isForegroundSet)
                {
                    SetForeground(surface, layout.GetBrush(runBrush) ?? brush);
                    isForegroundSet = true;
                }

                DrawColorGlyph(surface, glyph, face, placement, mode, blend, paint);
            }
        }
    }

    /// <summary>Draws a color glyph on an image of the plane of the drawing around its box, then that image in perspective.</summary>
    private void DrawProjectedColorGlyph(Brush brush, ColorGlyph glyph, FontFace face, in Affine2D placement, CompositeMode mode, BlendMode blend)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? glyph.XMin : glyph.XMax;
            var y = (corner & 2) == 0 ? glyph.YMin : glyph.YMax;
            var px = placement.TransformX(x, y);
            var py = placement.TransformY(x, y);
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        // A pixel of the image around the box: its edges are antialiased
        var pixel = 2 / _transformScale;
        var scope = BeginProjectedScope(RectangleD.FromLTRB(minX - pixel, minY - pixel, maxX + pixel, maxY + pixel), margin: 0);
        var isDrawn = false;
        try
        {
            scope.Session.DrawColorGlyph(brush, glyph, face, placement);
            isDrawn = true;
        }
        finally
        {
            EndProjectedScope(scope, isDrawn, mode, blend);
        }
    }

    /// <summary>Draws one color glyph, placed in the drawing, with the operator and the blend mode that combine it with what is under it.</summary>
    private void DrawColorGlyph(Brush brush, ColorGlyph glyph, FontFace face, Affine2D placement)
    {
        placement = ToCanvas(placement);
        if (glyph.IsMask)
        {
            // On the image of the plane, which is drawn with the operator and the blend mode of the brush
            DrawMaskGlyph(brush, glyph, placement, CompositeMode.SourceOver, BlendMode.Normal, paint: true);
            return;
        }

        var surface = _colorSurface ??= new ColorSurface(_image.Owner.Scope);
        if (!surface.Reserve(glyph, placement, Width, Height))
            return;

        SetForeground(surface, brush);
        DrawColorGlyph(surface, glyph, face, placement, CompositeMode.SourceOver, BlendMode.Normal, paint: true);
    }

    /// <summary>
    /// Gives the surface the color of the text: the one of a solid brush, or the gradient of a gradient brush, in canvas
    /// coordinates; black for the other brushes. The alpha of a solid color and the opacity of the brush fade the whole
    /// glyph, as browsers draw a color glyph with a translucent text color: the parts of the glyph that take the color
    /// of the text take it opaque.
    /// </summary>
    private void SetForeground(ColorSurface surface, Brush brush)
    {
        surface.Opacity = brush.Opacity;
        if (brush is SolidBrush solid)
        {
            var color = solid.Color;
            surface.Foreground = (color.R / 65535.0, color.G / 65535.0, color.B / 65535.0, 1);
            surface.ForegroundGradient = null;
            surface.Opacity = color.A / 65535.0 * brush.Opacity;
            return;
        }

        if (brush is not GradientBrush gradientBrush)
        {
            // An image, a pattern or a path gradient has no color of its own to give to a glyph that draws with its own colors: the
            // text is black
            surface.Foreground = (0, 0, 0, 1);
            surface.ForegroundGradient = null;
            return;
        }

        var gradient = _foregroundGradient ??= new GradientEvaluator(_image.Owner.Scope);
        gradient.PrepareColors(gradientBrush, ToCanvas(gradientBrush.Transform), Width);
        surface.ForegroundGradient = gradient;
    }

    private void DrawColorGlyph(ColorSurface surface, ColorGlyph glyph, FontFace face, in Affine2D placement, CompositeMode mode, BlendMode blend, bool paint)
    {
        var rasterizer = _rasterizer!;
        var numbers = glyph.Numbers;
        if (paint)
        {
            surface.Begin();
        }

        foreach (ref readonly var op in glyph.Ops)
        {
            switch (op.Kind)
            {
                case ColorOpKind.FillGlyph:
                    {
                        var hasCoverage = BeginOutline(face, op.Glyph, op.Transform < 0 ? placement : placement.Then(numbers.Slice(op.Transform, 6)));
                        ref readonly var fill = ref glyph.Paints[op.Paint];
                        if (!paint || !hasCoverage || !TryGetPaintSpace(placement, numbers, fill, out var inverse))
                            break;

                        while (rasterizer.MoveNext())
                        {
                            surface.Fill(rasterizer.Y, rasterizer.X, rasterizer.Coverage, glyph, fill, inverse);
                        }

                        break;
                    }

                case ColorOpKind.FillImage:
                    {
                        // The rectangle of the image, which the placement of the glyph can turn.
                        ref readonly var fill = ref glyph.Paints[op.Paint];
                        ReadOnlySpan<double> box = [glyph.XMin, glyph.YMin, glyph.XMax, glyph.YMax];
                        var hasCoverage = BeginBox(box, placement);
                        if (!paint || !hasCoverage || !placement.TryInvert(out var inverse))
                            break;

                        while (rasterizer.MoveNext())
                        {
                            surface.Fill(rasterizer.Y, rasterizer.X, rasterizer.Coverage, glyph, fill, inverse);
                        }

                        break;
                    }

                case ColorOpKind.FillClip:
                    {
                        ref readonly var fill = ref glyph.Paints[op.Paint];
                        if (paint && TryGetPaintSpace(placement, numbers, fill, out var inverse))
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            surface.FillClip(glyph, fill, inverse);
                        }

                        break;
                    }

                case ColorOpKind.PushClipGlyph or ColorOpKind.PushClipBox:
                    {
                        var transform = op.Transform < 0 ? placement : placement.Then(numbers.Slice(op.Transform, 6));
                        var hasCoverage = op.Kind == ColorOpKind.PushClipGlyph ? BeginOutline(face, op.Glyph, transform) : BeginBox(numbers.Slice(op.Paint, 4), transform);
                        if (!paint)
                            break;

                        surface.PushClip();
                        while (hasCoverage && rasterizer.MoveNext())
                        {
                            surface.AddClip(rasterizer.Y, rasterizer.X, rasterizer.Coverage);
                        }

                        break;
                    }

                case ColorOpKind.PopClip when paint:
                    surface.PopClip();
                    break;

                case ColorOpKind.PushLayer when paint:
                    surface.PushLayer();
                    break;

                case ColorOpKind.PopLayer when paint:
                    _cancellationToken.ThrowIfCancellationRequested();
                    surface.PopLayer((ColorCompositeMode)op.Paint);
                    break;
            }
        }

        if (!paint)
            return;

        // The glyph is complete: it is composited onto the frames, row by row, as a paint that has a color per pixel.
        for (var row = 0; row < surface.Height; row++)
        {
            if (!surface.GetRow(row, _frames[0], out var coverage, out var colors))
                continue;

            if (!_thumbnailRemoved)
            {
                _image.Metadata.RemoveStaleThumbnail();
                _thumbnailRemoved = true;
            }

            var x = surface.Left;
            if (_clipDepth != 0)
            {
                if (!ApplyClip(surface.Top + row, ref x, ref coverage, out var skipped))
                    continue;

                colors = colors.Slice(skipped * 4, coverage.Length * 4);
            }

            var source = new DrawingSource(colors, mode, blend, _transfer, _curves);
            MarkDrawn(x, surface.Top + row, x + coverage.Length, surface.Top + row + 1);
            for (var i = 0; i < _frames.Length; i++)
            {
                _frames[i].CompositePixels(_storages[i], surface.Top + row, x, coverage, source);
            }
        }
    }

    /// <summary>
    /// Draws a glyph that is a mask (a bitmap of several bits per pixel) with a brush, like a shape: the coverage of the
    /// rectangle of its image, multiplied by the opacity of the image, interpolated at the center of each pixel, is
    /// painted with the brush, so that every brush gives the glyph its colors.
    /// </summary>
    /// <param name="brush">The brush of the text.</param>
    /// <param name="glyph">The glyph, whose image is the mask.</param>
    /// <param name="placement">The placement of the glyph in the pixels of the canvas.</param>
    /// <param name="mode">The operator that combines the glyph with what is under it.</param>
    /// <param name="blend">The blend mode that mixes the color of the glyph with the color under it.</param>
    /// <param name="paint"><see langword="false"/> to only rent what the drawing needs.</param>
    private void DrawMaskGlyph(Brush brush, ColorGlyph glyph, in Affine2D placement, CompositeMode mode, BlendMode blend, bool paint)
    {
        _maskRow ??= ResampleWeights.Rent<uint>(_image.Owner.Scope, Width);
        ReadOnlySpan<double> box = [glyph.XMin, glyph.YMin, glyph.XMax, glyph.YMax];
        // The brush first: a pattern draws its tile, with the sweep the box then starts.
        if (!PrepareBrush(brush, out var color, out var evaluator) || !BeginBox(box, placement) || !placement.TryInvert(out var inverse) || !paint || _isPreparing)
            return;

        RemoveStaleThumbnail();
        var mask = glyph.Image!;
        var geometry = glyph.Numbers[..4];
        var row = unsafe(MemoryMarshal.Cast<byte, uint>(_maskRow.Span));
        var rasterizer = _rasterizer!;
        evaluator?.Begin();
        try
        {
            while (rasterizer.MoveNext())
            {
                var coverage = rasterizer.Coverage;
                var y = rasterizer.Y;
                var target = row[..coverage.Length];
                for (var i = 0; i < coverage.Length; i++)
                {
                    var px = rasterizer.X + i + 0.5;
                    var opacity = SampleOpacity(mask, geometry, inverse.TransformX(px, y + 0.5), inverse.TransformY(px, y + 0.5));
                    target[i] = (uint)Math.Round(coverage[i] * opacity, MidpointRounding.AwayFromZero);
                }

                PaintSpan(color, evaluator, mode, blend, CoverageRasterizer.FullCoverage, y, rasterizer.X, target, brush.Opacity);
            }
        }
        finally
        {
            evaluator?.End();
        }
    }

    /// <summary>Interpolates the opacity of an image at a point of the space of a glyph (bilinear, clamped at the edges).</summary>
    /// <param name="image">The image.</param>
    /// <param name="geometry">The left and the top of the image, and the width and the height of one of its pixels, in the units of the glyph.</param>
    /// <param name="x">The horizontal coordinate of the point.</param>
    /// <param name="y">The vertical coordinate of the point.</param>
    private static double SampleOpacity(ColorImage image, ReadOnlySpan<double> geometry, double x, double y)
    {
        // The position in pixels of the image, from the center of its top-left pixel; its rows go down.
        var u = ((x - geometry[0]) / geometry[2]) - 0.5;
        var v = ((geometry[1] - y) / geometry[3]) - 0.5;
        var column = Math.Floor(u);
        var line = Math.Floor(v);
        var fx = u - column;
        var fy = v - line;
        var x0 = (int)Math.Clamp(column, 0, image.Width - 1);
        var x1 = (int)Math.Clamp(column + 1, 0, image.Width - 1);
        var y0 = (int)Math.Clamp(line, 0, image.Height - 1);
        var y1 = (int)Math.Clamp(line + 1, 0, image.Height - 1);
        var pixels = image.Pixels;
        var opacity = ((1 - fx) * (1 - fy) * pixels[(((y0 * image.Width) + x0) * 4) + 3])
            + (fx * (1 - fy) * pixels[(((y0 * image.Width) + x1) * 4) + 3])
            + ((1 - fx) * fy * pixels[(((y1 * image.Width) + x0) * 4) + 3])
            + (fx * fy * pixels[(((y1 * image.Width) + x1) * 4) + 3]);
        return opacity / 255;
    }

    /// <summary>Gets the transformation from the pixels of the canvas to the space of a paint.</summary>
    /// <returns><see langword="false"/> when the paint is flattened to a line or a point: it draws nothing.</returns>
    private static bool TryGetPaintSpace(in Affine2D placement, ReadOnlySpan<double> numbers, in ColorPaint paint, out Affine2D inverse)
        => (paint.Transform < 0 ? placement : placement.Then(numbers.Slice(paint.Transform, 6))).TryInvert(out inverse);

    /// <summary>Builds the edges of the outline of a glyph and starts their sweep.</summary>
    /// <returns><see langword="false"/> when the outline covers nothing of the canvas.</returns>
    private bool BeginOutline(FontFace face, int glyph, in Affine2D transform)
    {
        var sink = BeginDeviceFill();
        var glyphSink = _glyphSink ??= new GlyphPathSink();
        glyphSink.Begin(sink, transform);
        var budget = new GlyphBudget(face.Data.Limits);
        face.DecodeOutline(glyph, glyphSink, _glyphScratch ??= new GlyphScratch(_image.Owner.Scope), ref budget);
        sink.End();
        return _edges!.Count != 0 && _rasterizer!.Begin(_edges, FillRule.NonZero, _cancellationToken);
    }

    /// <summary>Builds the edges of a box (its left, bottom, right and top, in the units of the glyph) and starts their sweep.</summary>
    private bool BeginBox(ReadOnlySpan<double> box, in Affine2D transform)
    {
        var sink = BeginDeviceFill();
        sink.MoveTo(transform.TransformX(box[0], box[1]), transform.TransformY(box[0], box[1]));
        sink.LineTo(transform.TransformX(box[0], box[3]), transform.TransformY(box[0], box[3]), smooth: false);
        sink.LineTo(transform.TransformX(box[2], box[3]), transform.TransformY(box[2], box[3]), smooth: false);
        sink.LineTo(transform.TransformX(box[2], box[1]), transform.TransformY(box[2], box[1]), smooth: false);
        sink.Close(smooth: false);
        sink.End();
        return _edges!.Count != 0 && _rasterizer!.Begin(_edges, FillRule.NonZero, _cancellationToken);
    }
}
