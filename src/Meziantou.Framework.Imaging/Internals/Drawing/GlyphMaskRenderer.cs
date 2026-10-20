using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Computes the coverage of a glyph in the pixels around the pixel of its origin: the outline placed with its origin at a
/// position in that pixel, and the stroke of a synthetic bold, rasterized on a frame of their own.
/// </summary>
/// <remarks>
/// The coverage depends on the linear part of the transformation, on the position of the origin in its pixel, on
/// antialiasing and on the bold, not on the pixel: a glyph whose mask is moved by whole pixels has the pixels the same
/// glyph has wherever its origin is in the same place of a pixel. A glyph whose box exceeds <see cref="MaxPixels"/>
/// pixels, or <see cref="MaxSide"/> pixels on a side, has no mask: it is drawn as a shape. The scratch is rented once.
/// </remarks>
internal sealed class GlyphMaskRenderer : IDisposable
{
    /// <summary>The largest side of a mask, in pixels.</summary>
    public const int MaxSide = 512;

    /// <summary>The largest number of pixels of a mask.</summary>
    public const int MaxPixels = 128 * 128;

    private readonly EdgeBuilder _edges;
    private readonly CoverageRasterizer _rasterizer;
    private readonly PooledBuffer _coverage;
    private readonly FillSink _fill;
    private readonly GlyphPathSink _glyphSink = new();
    private readonly RecordedOutlines.SingleRecorder _outline = new();
    private Stroker? _emboldener;

    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public GlyphMaskRenderer(AllocationScope scope)
    {
        _edges = new EdgeBuilder(scope, MaxSide, MaxSide);
        CoverageRasterizer? rasterizer = null;
        try
        {
            rasterizer = new CoverageRasterizer(scope, MaxSide, MaxSide);
            _coverage = ResampleWeights.Rent<uint>(scope, MaxPixels);
        }
        catch
        {
            rasterizer?.Dispose();
            _edges.Dispose();
            throw;
        }

        _rasterizer = rasterizer;
        _fill = new FillSink(_edges);
    }

    /// <summary>Gets the coverages of the last mask, row by row.</summary>
    public ReadOnlySpan<uint> Coverage => unsafe(MemoryMarshal.Cast<byte, uint>(_coverage.Span))[..(Width * Height)];

    /// <summary>Gets the column of the first pixel of the last mask, relative to the pixel of the origin.</summary>
    public int Left { get; private set; }

    /// <summary>Gets the row of the first pixel of the last mask, relative to the pixel of the origin.</summary>
    public int Top { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Computes the mask of a glyph.</summary>
    /// <param name="face">The font.</param>
    /// <param name="glyph">The glyph.</param>
    /// <param name="size">The size of the text, for the outline.</param>
    /// <param name="linear">The linear part of the transformation from font units to pixels (its translation is ignored).</param>
    /// <param name="fractionX">The position of the origin in its pixel, along x.</param>
    /// <param name="fractionY">The position of the origin in its pixel, along y.</param>
    /// <param name="boldWidth">The width of the stroke of a synthetic bold, in pixels, or 0.</param>
    /// <param name="isAliased">Whether the coverage is the one of the centers of the pixels.</param>
    /// <param name="scratch">The scratch of the outlines.</param>
    /// <param name="cancellationToken">The token checked by the sweep.</param>
    /// <param name="prepareOnly">
    /// Whether to stop once every buffer the mask needs is rented (the edges and the scratch of the sweep), before the sweep:
    /// <see cref="Coverage"/> is then not computed.
    /// </param>
    /// <returns><see langword="false"/> when the glyph is too large for a mask.</returns>
    public bool Render(FontFace face, int glyph, double size, in Affine2D linear, double fractionX, double fractionY, double boldWidth, bool isAliased, GlyphScratch scratch, CancellationToken cancellationToken, bool prepareOnly = false)
    {
        // The outline is decoded once, measured and recorded, then replayed for the contours and for the bold
        Left = Top = Width = Height = 0;
        _outline.Begin();
        var budget = new GlyphBudget(face.Data.Limits);
        face.DecodeOutline(glyph, size, _outline, scratch, ref budget);
        var bounds = _outline.Bounds;
        if (bounds.IsEmpty)
            return true;

        // The box of the control points, placed, with what the stroke of the bold reaches (a miter of 2) and a pixel
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? bounds.MinX : bounds.MaxX;
            var y = (corner & 2) == 0 ? bounds.MinY : bounds.MaxY;
            var px = (linear.A * x) + (linear.C * y) + fractionX;
            var py = (linear.B * x) + (linear.D * y) + fractionY;
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        var margin = boldWidth + 1;
        var left = Math.Floor(minX - margin);
        var top = Math.Floor(minY - margin);
        var width = Math.Ceiling(maxX + margin) - left;
        var height = Math.Ceiling(maxY + margin) - top;
        if (!(width <= MaxSide && height <= MaxSide && width * height <= MaxPixels))
            return false;

        Left = (int)left;
        Top = (int)top;
        Width = (int)width;
        Height = (int)height;
        var placement = new Affine2D(linear.A, linear.B, linear.C, linear.D, fractionX - left, fractionY - top);

        // The contours of the glyph, the ones that turn the other way reversed, then the stroke of the bold
        _edges.Reset();
        _glyphSink.Begin(_fill, placement);
        SendOutline(face, glyph, size, scratch);
        _fill.End();
        if (_glyphSink.SignedArea < 0)
        {
            _edges.NegateDirections(0);
        }

        if (boldWidth > 0)
        {
            var emboldener = _emboldener ??= new Stroker(_edges);
            emboldener.SetOutput(hasTransform: false, default, 1);
            emboldener.Configure(boldWidth, LineJoin.Miter, GlyphRun.BoldMiterLimit);
            _glyphSink.Begin(emboldener, placement);
            SendOutline(face, glyph, size, scratch);
            emboldener.End();
        }

        // The sweep rents nothing: Begin rents its scratch, and the next Begin resets what an unfinished sweep left
        if (prepareOnly)
        {
            _rasterizer.Begin(_edges, FillRule.NonZero, isAliased, cancellationToken);
            return true;
        }

        var coverage = unsafe(MemoryMarshal.Cast<byte, uint>(_coverage.Span))[..(Width * Height)];
        coverage.Clear();
        if (_rasterizer.Begin(_edges, FillRule.NonZero, isAliased, cancellationToken))
        {
            while (_rasterizer.MoveNext())
            {
                var y = _rasterizer.Y;
                if (y >= Height)
                    continue;

                var x = _rasterizer.X;
                var run = _rasterizer.Coverage;
                var count = Math.Min(run.Length, Width - x);
                if (count > 0)
                {
                    run[..count].CopyTo(coverage.Slice((y * Width) + x, count));
                }
            }
        }

        return true;
    }

    /// <summary>Sends the outline measured last to the glyph sink: replayed, or decoded again when it was too large to be recorded.</summary>
    private void SendOutline(FontFace face, int glyph, double size, GlyphScratch scratch)
    {
        if (_outline.IsRecorded)
        {
            _outline.Replay(_glyphSink);
            return;
        }

        var budget = new GlyphBudget(face.Data.Limits);
        face.DecodeOutline(glyph, size, _glyphSink, scratch, ref budget);
    }

    public void Dispose()
    {
        _edges.Dispose();
        _rasterizer.Dispose();
        _coverage.Dispose();
    }
}
