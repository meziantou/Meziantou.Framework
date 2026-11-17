using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The texts of a <see cref="DrawingCanvas.GlyphMasks(GlyphCache?, DrawingAction)"/> scope: drawn glyph by glyph, from masks.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> Inside the scope, a layout filled at a point is drawn as <see cref="FillGlyphs"/> draws a run: each
/// glyph on its own, over the ones before it, from its coverage around the pixel of its origin, its origin snapped to a
/// quarter of a pixel along the line and to a pixel across it. The glyph, its position and its outline (the one the layout
/// recorded, hinted when the layout is) are those of the layout, so the pixels are those <see cref="FillGlyphs"/> gives
/// the same glyphs at the same origins. The masks are kept in the cache of the scope: the one the caller gives, or one
/// that the session empties when the scope ends.
/// </para>
/// <para>
/// The rest of the text is drawn as outside the scope, in the same order: the backgrounds under it, the decorations of
/// each paint once the glyphs are drawn, then the glyphs a font draws with its own colors and the pens of the styles. A
/// transformation that turns, skews or is projective, and a layout with subpixel rendering, keep the fill of one shape.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    /// <summary>The most bytes the cache of a scope that was given none holds.</summary>
    private const long ScopeGlyphMaskBytes = 8L << 20;

    /// <summary>The cache of the masks of the texts of the scope, or <see langword="null"/> outside of a scope.</summary>
    private GlyphCache? _textGlyphMasks;

    /// <summary>The cache of the scopes of this session that were given none: emptied when the last of them ends.</summary>
    private GlyphCache? _scopeGlyphMasks;

    /// <summary>The number of scopes of this session that use <see cref="_scopeGlyphMasks"/> and have not ended.</summary>
    private int _scopeGlyphMaskUses;

    /// <summary>Draws the texts of the draw calls that follow from glyph masks kept in a cache.</summary>
    /// <param name="cache">The cache, or <see langword="null"/> for one that lives until the scope ends.</param>
    /// <returns>The cache of the scope that was set, or <see langword="null"/> when none was.</returns>
    public GlyphCache? SetGlyphMasks(GlyphCache? cache)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var previous = _textGlyphMasks;
        if (cache is null)
        {
            _textGlyphMasks = _scopeGlyphMasks ??= new GlyphCache(ScopeGlyphMaskBytes, _image.Owner.Scope);
            _scopeGlyphMaskUses++;
        }
        else
        {
            _textGlyphMasks = cache;
        }

        return previous;
    }

    /// <summary>Sets the cache back to what <see cref="SetGlyphMasks"/> returned: nothing is rented, and nothing fails.</summary>
    /// <remarks>
    /// The cache of this session is emptied, and its bytes released, when the last scope that uses it ends, even when the
    /// scope around it uses a cache inherited from another session (a layer, or the tile of a pattern).
    /// </remarks>
    public void RestoreGlyphMasks(GlyphCache? previous)
    {
        if (_textGlyphMasks is not null && ReferenceEquals(_textGlyphMasks, _scopeGlyphMasks) && --_scopeGlyphMaskUses == 0)
        {
            _scopeGlyphMasks.Clear();
        }

        _textGlyphMasks = previous;
    }

    /// <summary>Fills a layout at a point glyph by glyph, from masks, when the transformation keeps the axes or swaps them.</summary>
    /// <returns><see langword="false"/> when the glyphs cannot be masks: the layout is then filled as one shape.</returns>
    private bool TryFillTextFromMasks(Brush brush, TextLayout layout, PointD origin)
    {
        var swapsAxes = _hasTransform && _transform.M11 == 0 && _transform.M22 == 0;
        if (_isProjective || (_hasTransform && (_transform.M12 != 0 || _transform.M21 != 0) && !swapsAxes))
            return false;

        _cancellationToken.ThrowIfCancellationRequested();

        // The line goes along the y axis of the canvas for a vertical layout, or for a horizontal one turned a quarter
        var isLineVertical = swapsAxes ^ layout.IsVertical;

        // Every rental happens before the first pixel is written: the text is drawn a first time without being painted
        var cache = _textGlyphMasks;
        _isPreparing = true;
        try
        {
            try
            {
                DrawTextFromMasks(brush, layout, origin.X, origin.Y, isLineVertical, cache);
            }
            catch (ImageResourceLimitException) when (cache is { IsCharged: true })
            {
                // The masks the scope keeps leave no room for the buffers of this text: they are released, and the masks
                // of this text are not kept
                cache.Clear();
                cache = null;
                DrawTextFromMasks(brush, layout, origin.X, origin.Y, isLineVertical, cache);
            }
        }
        finally
        {
            _isPreparing = false;
        }

        DrawTextFromMasks(brush, layout, origin.X, origin.Y, isLineVertical, cache);
        return true;
    }

    private void DrawTextFromMasks(Brush brush, TextLayout layout, double originX, double originY, bool isLineVertical, GlyphCache? cache)
    {
        DrawTextBackgrounds(layout, originX, originY, measure: null, options: null);

        var scratch = _glyphScratch ??= new GlyphScratch(_image.Owner.Scope);
        Brush? preparedBrush = null;
        DrawingColor color = default;
        PaintEvaluator? evaluator = null;
        var isPaintable = false;
        foreach (ref readonly var run in layout.Runs)
        {
            var runBrush = layout.BrushCount == 0 ? brush : layout.GetBrush(run.Brush) ?? brush;
            var face = layout.GetFont(run.Font).Face;
            var scale = run.Size / face.UnitsPerEm;
            var boldWidth = run.EmboldenWidth;
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                // A glyph the font draws with its own colors is drawn with them once the other glyphs are drawn
                if (layout.GetColorGlyph(i) is not null)
                    continue;

                var position = layout.GetGlyphOrigin(i);
                var placement = run.GetTransform(scale, originX + position.X, originY + position.Y);
                var source = GlyphOutlineSource.FromLayout(layout, i, face, run.Size);
                if (TryGetGlyphMask(source, placement, boldWidth, isLineVertical, cache, scratch, out var mask, out var pixelX, out var pixelY))
                {
                    if (!ReferenceEquals(preparedBrush, runBrush))
                    {
                        isPaintable = PrepareBrush(runBrush, out color, out evaluator);
                        preparedBrush = runBrush;
                    }

                    if (isPaintable && !_isPreparing)
                    {
                        if (mask is null)
                        {
                            var renderer = _glyphMasks!;
                            PaintMask(color, evaluator, runBrush, pixelX + renderer.Left, pixelY + renderer.Top, renderer.Width, renderer.Height, renderer.Coverage, correction: null);
                        }
                        else
                        {
                            PaintMask(color, evaluator, runBrush, pixelX + mask.Left, pixelY + mask.Top, mask.Width, mask.Height, mask.Coverage, correction: null);
                        }
                    }

                    continue;
                }

                // Too large for a mask: filled as a shape where it is, which prepares the paint again
                FillGlyphOutline(runBrush, source, placement, boldWidth, scratch);
                preparedBrush = null;
            }
        }

        if (layout.HasDecorations)
        {
            if (layout.BrushCount == 0)
            {
                layout.EmitDecorations(BeginFill(), originX, originY, brush: -1);
                Render(brush, FillRule.NonZero);
            }
            else
            {
                for (var i = 0; i < layout.BrushCount; i++)
                {
                    layout.EmitDecorations(BeginFill(), originX, originY, i);
                    Render(layout.GetBrush(i) ?? brush, FillRule.NonZero);
                }
            }
        }

        DrawColorGlyphs(brush, layout, originX, originY, measure: null, options: null, paint: !_isPreparing);
        StrokeTextOutlines(layout, originX, originY, measure: null, options: null);
    }
}
