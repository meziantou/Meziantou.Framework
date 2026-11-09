using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The text filled with subpixel rendering (<see cref="TextLayoutOptions.SubpixelRendering"/>).</summary>
/// <remarks>
/// The glyphs are rasterized at three times the width of the canvas: the pixels of the canvas are transformed as usual,
/// then their horizontal coordinates are tripled. Each row of thirds of pixels is filtered into the coverage of the
/// colors of its pixels (<see cref="SubpixelTextKernels"/>), which goes through the clip and is composited a color at
/// a time on every frame.
/// </remarks>
internal sealed partial class DrawingSession
{
    private const int SubpixelsPerPixel = 3;

    private EdgeBuilder? _subpixelEdges;
    private CoverageRasterizer? _subpixelRasterizer;
    private FillSink? _subpixelFillSink;
    private HorizontalScaleSink? _subpixelScaleSink;
    private PooledBuffer? _subpixelCoverage;

    /// <summary>Fills a text with subpixel rendering.</summary>
    /// <returns><see langword="false"/> when the text, the paint, the canvas or the pixel format need grayscale antialiasing: nothing was drawn.</returns>
    private bool TryFillSubpixelText(Brush brush, TextLayout layout, PointD origin)
    {
        if (_isAliased || _transfer != 0 || layout.HasColorGlyphs || layout.HasBackgrounds || layout.OutlineCount != 0 || layout.HasEmboldenedGlyphs)
            return false;

        if (!SubpixelTextKernels.IsSupported(_frames[0].PixelFormat, out var isBlueFirst))
            return false;

        var paintCount = Math.Max(1, layout.BrushCount);
        for (var i = 0; i < paintCount; i++)
        {
            if (GetSubpixelPaint(brush, layout, i) is not SolidBrush { CompositeMode: CompositeMode.SourceOver, BlendMode: BlendMode.Normal })
                return false;
        }

        _cancellationToken.ThrowIfCancellationRequested();

        // Every rental happens before the first pixel is written: the shapes of several paints are swept a first time
        // without being painted, for the storage of the sweep to grow to the largest of them.
        var scope = _image.Owner.Scope;
        _subpixelEdges ??= new EdgeBuilder(scope, Width * SubpixelsPerPixel, Height);
        _subpixelRasterizer ??= new CoverageRasterizer(scope, Width * SubpixelsPerPixel, Height);
        _subpixelCoverage ??= ResampleWeights.Rent<uint>(scope, ((long)Width + 2) * SubpixelsPerPixel);
        if (_clipDepth != 0)
        {
            EnsureCoverageRow();
            EnsureFullCoverageRow();
        }

        if (paintCount > 1)
        {
            for (var i = 0; i < paintCount; i++)
            {
                BeginSubpixelShape(layout, origin, i);
            }
        }

        // The colors of the pixels of a screen whose stripes are in the order the samples are stored come first
        var isReversed = (layout.SubpixelRendering == TextSubpixelRendering.Bgr) != isBlueFirst;
        for (var i = 0; i < paintCount; i++)
        {
            if (!BeginSubpixelShape(layout, origin, i))
                continue;

            var solid = (SolidBrush)GetSubpixelPaint(brush, layout, i);
            var color = _frames[0].ConvertDrawingColor(ToImageColor(solid.Color));
            if (solid.Opacity != 1)
            {
                color = color with { Alpha = ScaleAlpha(color.Alpha, solid.Opacity) };
            }

            if (color.Alpha == 0)
                continue;

            RemoveStaleThumbnail();
            var rasterizer = _subpixelRasterizer;
            while (rasterizer.MoveNext())
            {
                PaintSubpixelRow(rasterizer.Y, rasterizer.X, rasterizer.Coverage, color, isReversed);
            }
        }

        return true;
    }

    private static Brush GetSubpixelPaint(Brush brush, TextLayout layout, int index)
        => layout.BrushCount == 0 ? brush : layout.GetBrush(index) ?? brush;

    /// <summary>Builds the edges of the glyphs and decorations of one paint, in thirds of pixels, and starts their sweep.</summary>
    /// <returns><see langword="false"/> when the shape covers no pixel.</returns>
    private bool BeginSubpixelShape(TextLayout layout, PointD origin, int paint)
    {
        var edges = _subpixelEdges!;
        edges.Reset();
        var fill = _subpixelFillSink ??= new FillSink(edges);
        var scale = _subpixelScaleSink ??= new HorizontalScaleSink();
        scale.Configure(fill, SubpixelsPerPixel);

        // The curves are cut three times finer: the thirds of pixels are three times narrower
        scale.Tolerance = _curveTolerance / SubpixelsPerPixel;
        fill.Tolerance = scale.Tolerance;
        var sink = BeginFill(scale);
        sink.Tolerance = scale.Tolerance;
        layout.Emit(sink, _glyphSink ??= new GlyphPathSink(), origin.X, origin.Y, edges, brush: layout.BrushCount == 0 ? -1 : paint);
        return edges.Count != 0 && _subpixelRasterizer!.Begin(edges, FillRule.NonZero, isAliased: false, _cancellationToken);
    }

    /// <summary>Filters a row of coverages of thirds of pixels and composites it on every frame.</summary>
    private void PaintSubpixelRow(int y, int start, ReadOnlySpan<uint> coverage, DrawingColor color, bool isReversed)
    {
        // The pixels the filter reaches: the thirds of the run, and two thirds on each side
        var first = Math.Max(0, start - SubpixelTextKernels.FilterReach) / SubpixelsPerPixel;
        var end = Math.Min(Width, ((start + coverage.Length + SubpixelTextKernels.FilterReach - 1) / SubpixelsPerPixel) + 1);
        if (first >= end)
            return;

        var colors = unsafe(MemoryMarshal.Cast<byte, uint>(_subpixelCoverage!.Span))[..((end - first) * SubpixelsPerPixel)];
        SubpixelTextKernels.Filter(coverage, start, first, colors);
        if (isReversed)
        {
            for (var i = 0; i < colors.Length; i += SubpixelsPerPixel)
            {
                (colors[i], colors[i + 2]) = (colors[i + 2], colors[i]);
            }
        }

        var x = first;
        if (_clipDepth != 0)
        {
            // The coverage of the clip, the same for the three colors of a pixel
            ReadOnlySpan<uint> clip = unsafe(MemoryMarshal.Cast<byte, uint>(_blurCoverage!.Span))[first..end];
            if (!ApplyClip(y, ref x, ref clip, out var skipped))
                return;

            colors = colors.Slice(skipped * SubpixelsPerPixel, clip.Length * SubpixelsPerPixel);
            for (var i = 0; i < clip.Length; i++)
            {
                for (var c = 0; c < SubpixelsPerPixel; c++)
                {
                    ref var value = ref colors[(i * SubpixelsPerPixel) + c];
                    value = (uint)((((ulong)value * clip[i]) + (CoverageRasterizer.FullCoverage / 2)) >> 16);
                }
            }
        }

        var count = colors.Length / SubpixelsPerPixel;
        MarkDrawn(x, y, x + count, y + 1);
        for (var i = 0; i < _frames.Length; i++)
        {
            SubpixelTextKernels.Composite(_storages[i].GetRowSpanCore(y), _frames[i].PixelFormat, x, colors, color);
        }
    }

    private void DisposeSubpixelText()
    {
        _subpixelRasterizer?.Dispose();
        _subpixelEdges?.Dispose();
        _subpixelCoverage?.Dispose();
        _subpixelRasterizer = null;
        _subpixelEdges = null;
        _subpixelFillSink = null;
        _subpixelCoverage = null;
    }
}
