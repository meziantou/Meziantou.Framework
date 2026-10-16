using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Runs of glyphs placed by the caller: the glyphs of a font at positions a text shaper gives.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> Each glyph is drawn on its own, over the ones before it: the glyphs of a translucent text that overlap
/// are composited one over the other, as browsers draw them. Under a transformation that keeps the axes (or swaps them),
/// the origin of a glyph on the canvas is moved to the nearest quarter of a pixel along the line and to the nearest pixel
/// across it (to the nearest pixel without antialiasing), as the glyph caches of browsers do, and the glyph is drawn
/// from its coverage in the pixels around the pixel of its origin (<see cref="GlyphMaskRenderer"/>), kept in a
/// <see cref="GlyphCache"/> when one is given: the pixels are the same with or without the cache. A glyph too large for a
/// mask, and every glyph under a transformation that turns, skews or is projective, is filled as a shape where it is.
/// </para>
/// <para>
/// A glyph the font draws with its own colors (<c>COLR</c>, <c>sbix</c>, <c>CBDT</c>) is drawn as a text draws it, from
/// its display list, kept in the cache too. A synthetic bold fills the glyphs with the stroke of their outline; a
/// synthetic oblique shears them.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private GlyphMaskRenderer? _glyphMasks;

    public void FillGlyphs(Brush brush, Font font, double size, ReadOnlySpan<PositionedGlyph> glyphs, GlyphRunOptions? options)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(font);
        ValidateGlyphs(font, size, glyphs);
        _cancellationToken.ThrowIfCancellationRequested();
        options ??= GlyphRunOptions.Default;

        // Every rental happens before the first pixel is written: the run is drawn a first time without being painted
        _isPreparing = true;
        try
        {
            FillGlyphsCore(brush, font.Face, size, glyphs, options);
        }
        finally
        {
            _isPreparing = false;
        }

        FillGlyphsCore(brush, font.Face, size, glyphs, options);
    }

    public void StrokeGlyphs(Pen pen, Font font, double size, ReadOnlySpan<PositionedGlyph> glyphs, GlyphRunOptions? options)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(font);
        ValidateGlyphs(font, size, glyphs);
        options ??= GlyphRunOptions.Default;
        var face = font.Face;
        var scale = size / face.UnitsPerEm;
        var shear = options.GetShear();
        var glyphSink = _glyphSink ??= new GlyphPathSink();
        var scratch = _glyphScratch ??= new GlyphScratch(_image.Owner.Scope);
        var sink = BeginStroke(pen);
        foreach (ref readonly var positioned in glyphs)
        {
            glyphSink.Begin(sink, Affine2D.ForGlyph(scale, positioned.Origin.X, positioned.Origin.Y, shear));
            var budget = new GlyphBudget(face.Data.Limits);
            face.DecodeOutline(positioned.Glyph, size, glyphSink, scratch, ref budget);
        }

        sink.End();
        RenderStroke(pen);
    }

    private static void ValidateGlyphs(Font font, double size, ReadOnlySpan<PositionedGlyph> glyphs)
    {
        DrawingGuard.Length(size, nameof(size));
        foreach (ref readonly var glyph in glyphs)
        {
            if ((uint)glyph.Glyph >= (uint)font.GlyphCount)
                throw new ArgumentOutOfRangeException(nameof(glyphs), glyph.Glyph, "A glyph is not a glyph of the font.");

            DrawingGuard.Point(glyph.Origin, nameof(glyphs));
        }
    }

    private void FillGlyphsCore(Brush brush, FontFace face, double size, ReadOnlySpan<PositionedGlyph> glyphs, GlyphRunOptions options)
    {
        var scale = size / face.UnitsPerEm;
        var shear = options.GetShear();
        var boldWidth = options.GetBoldWidth(size);
        var scratch = _glyphScratch ??= new GlyphScratch(_image.Owner.Scope);

        // On the pixels: under a transformation that keeps the axes or swaps them, the glyphs are masks
        var swapsAxes = _hasTransform && _transform.M11 == 0 && _transform.M22 == 0;
        var isMasked = !_isProjective && (!_hasTransform || (_transform.M12 == 0 && _transform.M21 == 0) || swapsAxes);
        var isForegroundSet = false;
        var isBrushPrepared = false;
        DrawingColor color = default;
        PaintEvaluator? evaluator = null;
        var isPaintable = false;
        foreach (ref readonly var positioned in glyphs)
        {
            var glyph = positioned.Glyph;
            var placement = Affine2D.ForGlyph(scale, positioned.Origin.X, positioned.Origin.Y, shear);
            if (face.HasColorGlyphs && GetColorGlyph(face, glyph, options.Palette, size, options.Cache, scratch) is { } colorGlyph)
            {
                DrawRunColorGlyph(brush, colorGlyph, face, placement, ref isForegroundSet);
                continue;
            }

            if (isMasked && TryGetGlyphMask(face, glyph, size, placement, boldWidth, swapsAxes, options.Cache, scratch, out var mask, out var pixelX, out var pixelY))
            {
                if (!isBrushPrepared)
                {
                    isPaintable = PrepareBrush(brush, out color, out evaluator);
                    isBrushPrepared = true;
                }

                if (isPaintable && !_isPreparing)
                {
                    var opacity = brush is ImageBrush image ? image.OpacityCoverage : CoverageRasterizer.FullCoverage;
                    if (mask is null)
                    {
                        var renderer = _glyphMasks!;
                        PaintMask(color, evaluator, brush.CompositeMode, brush.BlendMode, opacity, pixelX + renderer.Left, pixelY + renderer.Top, renderer.Width, renderer.Height, renderer.Coverage);
                    }
                    else
                    {
                        PaintMask(color, evaluator, brush.CompositeMode, brush.BlendMode, opacity, pixelX + mask.Left, pixelY + mask.Top, mask.Width, mask.Height, mask.Coverage);
                    }
                }

                continue;
            }

            FillGlyphOutline(brush, face, glyph, size, placement, boldWidth, scratch);
            isBrushPrepared = false;
        }
    }

    /// <summary>
    /// Gets the coverage of a glyph from the cache, or computes it: in the renderer (<paramref name="mask"/> is then
    /// <see langword="null"/>), copied to the cache when there is one.
    /// </summary>
    /// <returns><see langword="false"/> when the glyph is too large for a mask, or its origin is too far from the canvas.</returns>
    private bool TryGetGlyphMask(FontFace face, int glyph, double size, in Affine2D placement, double boldWidth, bool swapsAxes, GlyphCache? cache, GlyphScratch scratch, out GlyphMask? mask, out int pixelX, out int pixelY)
    {
        mask = null;
        pixelX = 0;
        pixelY = 0;

        // The origin on the canvas: a quarter of a pixel along the line, a pixel across it; a pixel without antialiasing
        var canvas = ToCanvas(placement);
        var along = _isAliased ? 1 : 4;
        var x = Snap(canvas.Tx, swapsAxes ? 1 : along);
        var y = Snap(canvas.Ty, swapsAxes ? along : 1);
        const double Limit = 1 << 28;
        if (!(Math.Abs(x) < Limit && Math.Abs(y) < Limit))
            return false;

        var left = Math.Floor(x);
        var top = Math.Floor(y);
        pixelX = (int)left;
        pixelY = (int)top;
        var fractionX = x - left;
        var fractionY = y - top;
        var deviceBold = boldWidth * (_hasTransform ? _transformScale : 1);
        var key = new GlyphCacheKey(face, glyph, IsColor: false, size, Palette: 0, canvas.A, canvas.B, canvas.C, canvas.D, (int)(fractionX * 4), (int)(fractionY * 4), _isAliased, deviceBold);
        if (cache is not null && cache.TryGet(key, out var value))
        {
            mask = (GlyphMask)value!;
            return !ReferenceEquals(mask, GlyphMask.TooLarge);
        }

        var renderer = _glyphMasks ??= new GlyphMaskRenderer(_image.Owner.Scope);
        if (!renderer.Render(face, glyph, size, canvas, fractionX, fractionY, deviceBold, _isAliased, scratch, _cancellationToken))
        {
            cache?.Add(key, GlyphMask.TooLarge, GlyphMask.TooLarge.Bytes);
            return false;
        }

        if (cache is not null)
        {
            mask = new GlyphMask(renderer.Left, renderer.Top, renderer.Width, renderer.Height, renderer.Coverage.ToArray());
            cache.Add(key, mask, mask.Bytes);
        }

        return true;

        static double Snap(double value, int positions) => Math.Floor((value * positions) + 0.5) / positions;
    }

    /// <summary>Paints the coverages of a mask at a position of the canvas: row by row, through the clip and the opacity.</summary>
    private void PaintMask(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, BlendMode blend, uint opacity, int left, int top, int width, int height, ReadOnlySpan<uint> coverage)
    {
        evaluator?.Begin();
        try
        {
            RemoveStaleThumbnail();
            for (var row = 0; row < height; row++)
            {
                var y = top + row;
                if (y < 0 || y >= Height)
                    continue;

                // The covered pixels of the row on the canvas
                var start = Math.Max(0, -left);
                var end = Math.Min(width, Width - left);
                var values = coverage.Slice(row * width, width);
                while (start < end && values[start] == 0)
                {
                    start++;
                }

                while (end > start && values[end - 1] == 0)
                {
                    end--;
                }

                if (start < end)
                {
                    PaintSpan(color, evaluator, mode, blend, opacity, y, left + start, values[start..end]);
                }
            }
        }
        finally
        {
            evaluator?.End();
        }
    }

    /// <summary>Fills a glyph as a shape where it is, thickened by a synthetic bold.</summary>
    private void FillGlyphOutline(Brush brush, FontFace face, int glyph, double size, in Affine2D placement, double boldWidth, GlyphScratch scratch)
    {
        var glyphSink = _glyphSink ??= new GlyphPathSink();
        var sink = BeginFill();
        var edges = _edges!;
        glyphSink.Begin(sink, placement);
        var budget = new GlyphBudget(face.Data.Limits);
        face.DecodeOutline(glyph, size, glyphSink, scratch, ref budget);
        if (glyphSink.SignedArea < 0)
        {
            edges.NegateDirections(0);
        }

        var coversFrame = false;
        if (boldWidth > 0)
        {
            // The fill of the glyph is complete before the edges of its stroke follow it
            sink.End();
            var emboldener = _stroker ??= new Stroker(edges);
            if (UsesProjectiveGeometry)
            {
                emboldener.SetOutput(GeometryView.Inflate(0, DrawingGuard.MaxCoordinate));
            }
            else
            {
                emboldener.SetOutput(_hasTransform, _transformMatrix, _transformScale, _originX, _originY, IsRangeChecked);
            }

            emboldener.Configure(boldWidth, LineJoin.Miter, GlyphRun.BoldMiterLimit);
            glyphSink.Begin(emboldener, placement);
            budget = new GlyphBudget(face.Data.Limits);
            face.DecodeOutline(glyph, size, glyphSink, scratch, ref budget);
            emboldener.End();
            coversFrame = emboldener.CoversFrame;
        }

        sink.End();
        FillFrameWhenCovered(coversFrame);
        Render(brush, FillRule.NonZero);
    }

    /// <summary>Gets the display list of a glyph the font draws with its own colors, from the cache or built; <see langword="null"/> for a glyph that is an outline.</summary>
    private static ColorGlyph? GetColorGlyph(FontFace face, int glyph, int palette, double size, GlyphCache? cache, GlyphScratch scratch)
    {
        var isPainted = face.IsColorGlyph(glyph);
        var key = new GlyphCacheKey(face, glyph, IsColor: true, isPainted ? 0 : size, isPainted ? palette : 0, 0, 0, 0, 0, 0, 0, IsAliased: false, 0);
        if (cache is not null && cache.TryGet(key, out var value))
            return (ColorGlyph?)value;

        ColorGlyph? colorGlyph = null;
        var budget = new GlyphBudget(face.Data.Limits);
        if (isPainted)
        {
            colorGlyph = face.BuildColorGlyph(glyph, palette, scratch, ref budget);
        }
        else if (face.TryGetBitmapGlyph(glyph, size, out var bitmap))
        {
            colorGlyph = ColorImage.CreateGlyph(face, bitmap, size);
        }

        cache?.Add(key, colorGlyph, GetBytes(colorGlyph));
        return colorGlyph;

        static long GetBytes(ColorGlyph? glyph) => glyph is null
            ? 64
            : 256 + (32L * glyph.Ops.Length) + (64L * glyph.Paints.Length) + (32L * glyph.Stops.Length) + (8L * glyph.Numbers.Length) + (4L * (glyph.Image?.Pixels.Length ?? 0));
    }

    /// <summary>Draws a color glyph of a run: in the pixels of the canvas, or on an image of the plane under a projective transformation.</summary>
    private void DrawRunColorGlyph(Brush brush, ColorGlyph glyph, FontFace face, in Affine2D placement, ref bool isForegroundSet)
    {
        if (_isProjective)
        {
            if (!_isPreparing && !glyph.IsEmpty)
            {
                DrawProjectedColorGlyph(brush, glyph, face, placement, brush.CompositeMode, brush.BlendMode);
            }

            return;
        }

        var canvas = ToCanvas(placement);
        var surface = _colorSurface ??= new ColorSurface(_image.Owner.Scope);
        if (!surface.Reserve(glyph, canvas, Width, Height))
            return;

        if (!isForegroundSet)
        {
            SetForeground(surface, brush);
            isForegroundSet = true;
        }

        DrawColorGlyph(surface, glyph, face, canvas, brush.CompositeMode, brush.BlendMode, paint: !_isPreparing);
    }
}
