using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Clipping: the part of the canvas the draw calls of a callback can change.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> A clip gives each pixel a coverage, from 0 to <see cref="CoverageRasterizer.FullCoverage"/>: the area
/// of the pixel inside the clip path, as for a fill. The coverage of a shape drawn under a clip is multiplied by it:
/// <c>round(q m / 65536)</c>, to nearest with ties upward. A clip inside another one has the product of both coverages,
/// rounded the same way. A rectangle on whole coordinates is exact: its pixels are in or out.
/// </para>
/// <para>
/// A clip that is not such a rectangle keeps its coverages in a mask over the pixels it can let through, rented when
/// the clip starts: nothing is rented by the draw calls under it. The masks are kept, one per depth, until the session
/// ends.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private readonly List<ClipLevel> _clipLevels = [];
    private int _clipDepth;
    private PooledBuffer? _clipRow;

    // The colors of a paint whose alpha the opacity of its brush multiplies
    private PooledBuffer? _alphaRow;

    public void PushClip(RectangleD rectangle)
    {
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        if (_hasTransform || _isProjective || !IsWhole(rectangle.Left) || !IsWhole(rectangle.Top) || !IsWhole(rectangle.Right) || !IsWhole(rectangle.Bottom))
        {
            var sink = BeginFill();
            AddRectangle(sink, rectangle);
            sink.End();
            PushClipFromEdges(FillRule.NonZero, rectangle, isInverted: false);
            return;
        }

        // Whole pixels: the clip above, restricted to the rectangle
        _cancellationToken.ThrowIfCancellationRequested();
        GetClipBounds(out var left, out var top, out var right, out var bottom, out var maskLevel);
        var level = GetNextLevel();
        level.Left = (int)Math.Max(left, rectangle.Left);
        level.Top = (int)Math.Max(top, rectangle.Top);
        level.Right = (int)Math.Min(right, rectangle.Right);
        level.Bottom = (int)Math.Min(bottom, rectangle.Bottom);
        level.MaskLevel = maskLevel;
        _clipDepth++;
    }

    public void PushClip(VectorPath path, FillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (fillRule is not (FillRule.NonZero or FillRule.EvenOdd))
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not valid.");

        PathFlattener.Flatten(path, BeginFill());
        PushClipFromEdges(fillRule, path.Bounds, isInverted: false);
    }

    public void PopClip() => _clipDepth--;

    /// <summary>Starts a clip from the edges of the call: rasterizes them into the mask of the next depth.</summary>
    /// <param name="fillRule">The fill rule of the clip.</param>
    /// <param name="shapeBounds">A rectangle that contains the clip, in the coordinates of the drawing.</param>
    /// <param name="isInverted">
    /// <see langword="true"/> for a clip that lets through what is outside the edges, within the rectangle: the
    /// coverage of a pixel is then what is missing for the edges to cover it entirely.
    /// </param>
    private void PushClipFromEdges(FillRule fillRule, RectangleD shapeBounds, bool isInverted)
    {
        shapeBounds = ToCanvasBounds(shapeBounds);
        GetClipBounds(out var left, out var top, out var right, out var bottom, out var parentMaskLevel);
        left = (int)Math.Max(left, Math.Floor(shapeBounds.Left));
        top = (int)Math.Max(top, Math.Floor(shapeBounds.Top));
        right = (int)Math.Min(right, Math.Ceiling(shapeBounds.Right));
        bottom = (int)Math.Min(bottom, Math.Ceiling(shapeBounds.Bottom));
        var level = GetNextLevel();
        var rasterizer = _rasterizer!;
        if (right <= left || bottom <= top || (_edges!.Count == 0 && !isInverted))
        {
            // Nothing gets through
            level.Left = level.Top = level.Right = level.Bottom = 0;
            level.MaskLevel = -1;
            _clipDepth++;
            return;
        }

        var width = right - left;
        var height = bottom - top;
        level.Reserve(_image.Owner.Scope, (long)width * height);
        EnsureCoverageRow();
        var mask = level.Mask[..(width * height)];
        mask.Clear();
        var parent = parentMaskLevel < 0 ? null : _clipLevels[parentMaskLevel];
        if (isInverted)
        {
            // The coverages of the edges first, then what is missing of each, under the clip above
            parent = null;
        }

        if (_edges.Count != 0 && rasterizer.Begin(_edges, fillRule, _isAliased, _cancellationToken))
        {
            while (rasterizer.MoveNext())
            {
                var y = rasterizer.Y;
                if (y < top || y >= bottom)
                    continue;

                var x = rasterizer.X;
                var coverage = rasterizer.Coverage;
                var start = Math.Max(x, left);
                var end = Math.Min(x + coverage.Length, right);
                if (start >= end)
                    continue;

                var row = mask.Slice(((y - top) * width) + (start - left), end - start);
                coverage.Slice(start - x, end - start).CopyTo(row);

                if (parent is not null)
                {
                    Multiply(row, parent.Mask.Slice(((y - parent.MaskTop) * parent.MaskWidth) + (start - parent.MaskLeft), row.Length), row);
                }
            }
        }

        if (isInverted)
        {
            parent = parentMaskLevel < 0 ? null : _clipLevels[parentMaskLevel];
            for (var y = 0; y < height; y++)
            {
                var row = mask.Slice(y * width, width);
                foreach (ref var value in row)
                {
                    value = CoverageRasterizer.FullCoverage - value;
                }

                if (parent is not null)
                {
                    Multiply(row, parent.Mask.Slice(((top + y - parent.MaskTop) * parent.MaskWidth) + (left - parent.MaskLeft), width), row);
                }
            }
        }

        level.Left = level.MaskLeft = left;
        level.Top = level.MaskTop = top;
        level.Right = right;
        level.Bottom = bottom;
        level.MaskWidth = width;
        level.MaskLevel = _clipDepth;
        _clipDepth++;
    }

    /// <summary>
    /// Restricts a run of covered pixels to the clip: removes the pixels outside its bounds and multiplies the coverages
    /// by its mask.
    /// </summary>
    /// <param name="y">The row of the run.</param>
    /// <param name="x">The first column of the run; moved to the first column the clip keeps.</param>
    /// <param name="coverage">The coverages of the run; replaced by those of the pixels the clip keeps.</param>
    /// <param name="skipped">The number of pixels removed from the start of the run.</param>
    /// <returns><see langword="false"/> when nothing of the run gets through.</returns>
    private bool ApplyClip(int y, ref int x, ref ReadOnlySpan<uint> coverage, out int skipped)
    {
        skipped = 0;
        var level = _clipLevels[_clipDepth - 1];
        if (y < level.Top || y >= level.Bottom)
            return false;

        var start = Math.Max(x, level.Left);
        var end = Math.Min(x + coverage.Length, level.Right);
        if (start >= end)
            return false;

        skipped = start - x;
        coverage = coverage.Slice(skipped, end - start);
        x = start;
        if (level.MaskLevel < 0)
            return true;

        var owner = _clipLevels[level.MaskLevel];
        var product = unsafe(MemoryMarshal.Cast<byte, uint>(_clipRow!.Span))[..coverage.Length];
        var any = Multiply(coverage, owner.Mask.Slice(((y - owner.MaskTop) * owner.MaskWidth) + (x - owner.MaskLeft), coverage.Length), product);
        coverage = product;
        return any;
    }

    /// <summary>Multiplies the coverages of a run by an opacity, into the row of the session.</summary>
    private ReadOnlySpan<uint> ApplyOpacity(ReadOnlySpan<uint> coverage, uint opacity)
    {
        // The run may already be in that row, after a clip: each value is read before it is written
        var product = unsafe(MemoryMarshal.Cast<byte, uint>(_clipRow!.Span))[..coverage.Length];
        for (var i = 0; i < coverage.Length; i++)
        {
            product[i] = (uint)((((ulong)coverage[i] * opacity) + (CoverageRasterizer.FullCoverage / 2)) >> 16);
        }

        return product;
    }

    /// <summary>Rents the row the products of coverages are written to; a draw call that needs it rents it before it changes a pixel.</summary>
    private void EnsureCoverageRow() => _clipRow ??= ResampleWeights.Rent<uint>(_image.Owner.Scope, Width);

    /// <summary>Multiplies coverages: <c>round(a b / 65536)</c>, to nearest with ties upward.</summary>
    /// <returns><see langword="false"/> when every product is zero.</returns>
    private static bool Multiply(ReadOnlySpan<uint> coverage, ReadOnlySpan<uint> mask, Span<uint> product)
    {
        var any = false;
        for (var i = 0; i < coverage.Length; i++)
        {
            var value = mask[i] == CoverageRasterizer.FullCoverage ? coverage[i] : (uint)((((ulong)coverage[i] * mask[i]) + (CoverageRasterizer.FullCoverage / 2)) >> 16);
            product[i] = value;
            any |= value != 0;
        }

        return any;
    }

    /// <summary>Gets the pixels the current clip can let through, and the depth of the mask that gives their coverage, or -1.</summary>
    private void GetClipBounds(out int left, out int top, out int right, out int bottom, out int maskLevel)
    {
        if (_clipDepth == 0)
        {
            left = 0;
            top = 0;
            right = Width;
            bottom = Height;
            maskLevel = -1;
            return;
        }

        var level = _clipLevels[_clipDepth - 1];
        left = level.Left;
        top = level.Top;
        right = level.Right;
        bottom = level.Bottom;
        maskLevel = level.MaskLevel;
    }

    private ClipLevel GetNextLevel()
    {
        if (_clipDepth == _clipLevels.Count)
        {
            _clipLevels.Add(new ClipLevel());
        }

        return _clipLevels[_clipDepth];
    }

    private void DisposeClips()
    {
        foreach (var level in _clipLevels)
        {
            level.Dispose();
        }

        _clipLevels.Clear();
        _clipDepth = 0;
        _clipRow?.Dispose();
        _alphaRow?.Dispose();
        _alphaRow = null;
        _clipRow = null;
    }

    private static bool IsWhole(double value) => value == Math.Floor(value);

    /// <summary>A clip of the stack: the pixels it can let through and, when they are not all let through entirely, where their coverages are.</summary>
    private sealed class ClipLevel : IDisposable
    {
        private PooledBuffer? _buffer;
        private long _capacity;

        public int Left { get; set; }

        public int Top { get; set; }

        public int Right { get; set; }

        public int Bottom { get; set; }

        /// <summary>Gets or sets the depth of the clip whose mask gives the coverages of this one (itself, or a clip above it that a rectangle of whole pixels restricts), or -1 when every pixel is let through entirely.</summary>
        public int MaskLevel { get; set; } = -1;

        /// <summary>Gets or sets the column of the first pixel of the mask of this clip.</summary>
        public int MaskLeft { get; set; }

        public int MaskTop { get; set; }

        public int MaskWidth { get; set; }

        /// <summary>Gets the coverages of the mask of this clip, row by row.</summary>
        public Span<uint> Mask => unsafe(MemoryMarshal.Cast<byte, uint>(_buffer!.Span));

        /// <summary>Makes sure the mask can hold a number of pixels; its content is not kept.</summary>
        /// <exception cref="ImageResourceLimitException">The mask exceeds the allocation limit.</exception>
        public void Reserve(AllocationScope scope, long pixels)
        {
            if (pixels <= _capacity)
                return;

            _buffer?.Dispose();
            _buffer = null;
            _capacity = 0;
            _buffer = ResampleWeights.Rent<uint>(scope, pixels);
            _capacity = pixels;
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
            _capacity = 0;
        }
    }
}
