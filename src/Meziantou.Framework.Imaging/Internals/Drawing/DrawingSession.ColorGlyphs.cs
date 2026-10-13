using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The glyphs a font draws with its own colors.</content>
internal sealed partial class DrawingSession
{
    private ColorSurface? _colorSurface;
    private GradientEvaluator? _foregroundGradient;

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
            layout.GetRun(run, out var face, out var scale, out var start, out var count, out var runBrush);
            var mode = (layout.GetBrush(runBrush) ?? brush).CompositeMode;
            var isForegroundSet = false;
            for (var i = start; i < start + count; i++)
            {
                if (layout.GetColorGlyph(i) is not { } glyph)
                    continue;

                Affine2D placement;
                if (measure is null)
                {
                    placement = layout.GetGlyphTransform(i, scale, originX, originY);
                }
                else if (!layout.TryGetGlyphTransformOnPath(i, scale, measure, options!, out placement))
                {
                    continue;
                }

                if (!surface.Reserve(glyph, placement, Width, Height))
                    continue;

                if (!isForegroundSet)
                {
                    SetForeground(surface, layout.GetBrush(runBrush) ?? brush);
                    isForegroundSet = true;
                }

                DrawColorGlyph(surface, glyph, face, placement, mode, paint);
            }
        }
    }

    /// <summary>Gives the surface the color of the text: the one of a solid brush, or the gradient of a gradient brush, in canvas coordinates.</summary>
    private void SetForeground(ColorSurface surface, Brush brush)
    {
        if (brush is SolidBrush solid)
        {
            var color = solid.Color;
            surface.Foreground = (color.R / 65535.0, color.G / 65535.0, color.B / 65535.0, color.A / 65535.0);
            surface.ForegroundGradient = null;
            return;
        }

        var gradient = _foregroundGradient ??= new GradientEvaluator(_image.Owner.Scope);
        gradient.PrepareColors((GradientBrush)brush, Width);
        surface.ForegroundGradient = gradient;
    }

    private void DrawColorGlyph(ColorSurface surface, ColorGlyph glyph, FontFace face, in Affine2D placement, CompositeMode mode, bool paint)
    {
        var rasterizer = _rasterizer!;
        var numbers = glyph.Numbers.AsSpan();
        if (paint)
        {
            surface.Begin();
        }

        foreach (ref readonly var op in glyph.Ops.AsSpan())
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

            var source = new DrawingSource(colors, mode);
            for (var i = 0; i < _frames.Length; i++)
            {
                _frames[i].CompositePixels(_storages[i], surface.Top + row, x, coverage, source);
            }
        }
    }

    /// <summary>Gets the transformation from the pixels of the canvas to the space of a paint.</summary>
    /// <returns><see langword="false"/> when the paint is flattened to a line or a point: it draws nothing.</returns>
    private static bool TryGetPaintSpace(in Affine2D placement, ReadOnlySpan<double> numbers, in ColorPaint paint, out Affine2D inverse)
        => (paint.Transform < 0 ? placement : placement.Then(numbers.Slice(paint.Transform, 6))).TryInvert(out inverse);

    /// <summary>Builds the edges of the outline of a glyph and starts their sweep.</summary>
    /// <returns><see langword="false"/> when the outline covers nothing of the canvas.</returns>
    private bool BeginOutline(FontFace face, int glyph, in Affine2D transform)
    {
        var sink = BeginFill();
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
        var sink = BeginFill();
        sink.MoveTo(transform.TransformX(box[0], box[1]), transform.TransformY(box[0], box[1]));
        sink.LineTo(transform.TransformX(box[0], box[3]), transform.TransformY(box[0], box[3]), smooth: false);
        sink.LineTo(transform.TransformX(box[2], box[3]), transform.TransformY(box[2], box[3]), smooth: false);
        sink.LineTo(transform.TransformX(box[2], box[1]), transform.TransformY(box[2], box[1]), smooth: false);
        sink.Close(smooth: false);
        sink.End();
        return _edges!.Count != 0 && _rasterizer!.Begin(_edges, FillRule.NonZero, _cancellationToken);
    }
}
