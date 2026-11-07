using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The frames of an animation encoder that writes each frame as the region of the canvas that changes (the encoder side of
/// <see cref="AnimationCompositor"/>), shared by the GIF, APNG and WebP encoders. It keeps the displayed frame of the last
/// frame it was given, and the canvas that frame was drawn on when the format can restore it, then chooses for each new
/// frame the disposal of the previous frame, the region and the blending that reproduce the new frame exactly with the
/// smallest region.
/// </summary>
/// <remarks>
/// <para>
/// The pixels are straight-alpha RGBA rows of 4 bytes (<see cref="PixelFormat.Rgba32"/>) or 8 bytes
/// (<see cref="PixelFormat.Rgba64"/>, native-endian samples), in the form the encoder writes: a pixel is unchanged when its
/// bytes equal the bytes of the canvas, a cleared canvas pixel is transparent black (all bytes zero), and a fully
/// transparent pixel of another color is a change.
/// </para>
/// <para>
/// For a frame after the first one, <see cref="BeginCompare"/> sets up one candidate per disposal of the previous frame the
/// format allows: <see cref="AnimationDisposal.None"/> (the displayed frame), <see cref="AnimationDisposal.ClearToTransparent"/>
/// (the displayed frame with the region of the previous frame cleared) and <see cref="AnimationDisposal.RestorePrevious"/>
/// (the canvas the previous frame was drawn on; not after the first frame, which is drawn on a cleared canvas). Each row
/// of the frame (<see cref="CompareRow"/>) extends the bounding box of the pixels that differ from each candidate canvas.
/// OVER (unchanged pixels written fully transparent) is exact when every pixel that differs is opaque, or, where the format
/// composites it exactly, visible on a fully transparent canvas pixel. <see cref="TryChoose"/> keeps the valid candidate of
/// the smallest region (OVER, then the order above, on a tie); a frame identical to its canvas is a 1x1 region.
/// </para>
/// <para>
/// A format without SOURCE blending (GIF) cannot make a visible pixel transparent: when no candidate is exact, the region
/// of the previous frame, which such an encoder writes only once this choice is made, grows to cover the pixels that
/// become transparent, the previous frame is disposed with <see cref="AnimationDisposal.ClearToTransparent"/>, and the
/// frame is compared again (<see cref="TryChoose"/> returns <see langword="false"/> once).
/// </para>
/// <para>
/// <see cref="BeginCommit"/> then applies the chosen disposal to the private canvases, and <see cref="CommitRow"/> copies
/// the rows of the frame, which becomes the displayed frame. The first frame is committed without a comparison: it covers
/// the canvas and is drawn on a cleared canvas.
/// </para>
/// </remarks>
internal sealed class AnimationFrameDelta : IDisposable
{
    private readonly AnimationDeltaRules _rules;
    private readonly int _bytesPerPixel;
    private readonly int _alphaOffset;
    private readonly int _alphaBytes;
    private readonly Candidate[] _candidates = new Candidate[3];
    private AnimationCanvas? _displayed;
    private AnimationCanvas? _canvas;
    private Rectangle _lastRegion;
    private int _candidateCount;
    private bool _enlarged;
    private int _chosen = -1;
    private AnimationFramePlan _plan;

    /// <summary>Creates the state of an animation and rents its canvases.</summary>
    /// <param name="scope">The writer scope, charged for the canvases.</param>
    /// <param name="width">The canvas width.</param>
    /// <param name="height">The canvas height.</param>
    /// <param name="rules">What the format can write.</param>
    /// <exception cref="ImageResourceLimitException">The canvases exceed the allocation limit.</exception>
    public AnimationFrameDelta(AllocationScope scope, int width, int height, AnimationDeltaRules rules)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (rules.BytesPerPixel is not (4 or 8))
            throw new ArgumentOutOfRangeException(nameof(rules), rules.BytesPerPixel, "The pixels have 4 or 8 bytes.");

        if (!rules.SourceBlend && !rules.OverBlend)
            throw new ArgumentException("A format blends its frames with SOURCE or OVER.", nameof(rules));

        _rules = rules;
        OverBlend = rules.OverBlend;
        _bytesPerPixel = rules.BytesPerPixel;
        _alphaOffset = _bytesPerPixel == 4 ? 3 : 6;
        _alphaBytes = _bytesPerPixel / 4;
        Width = width;
        Height = height;
        try
        {
            _displayed = new AnimationCanvas(scope, width, height, _bytesPerPixel);
            if (rules.RestorePrevious || rules.EnlargePreviousRegion)
            {
                _canvas = new AnimationCanvas(scope, width, height, _bytesPerPixel);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Gets or sets a value indicating whether unchanged pixels can be written fully transparent
    /// (<see cref="AnimationDeltaRules.OverBlend"/>, for a format whose transparent value is only known once its header is
    /// chosen); a format without SOURCE blending always has it.
    /// </summary>
    public bool OverBlend
    {
        get;
        set => field = value || !_rules.SourceBlend;
    }

    /// <summary>Gets the number of committed frames.</summary>
    public int CommittedFrames { get; private set; }

    /// <summary>Gets the region of the last committed frame.</summary>
    public Rectangle LastRegion => _lastRegion;

    /// <summary>Gets the displayed frame of the last committed frame.</summary>
    public AnimationCanvas Displayed => _displayed ?? throw new ObjectDisposedException(nameof(AnimationFrameDelta));

    /// <summary>
    /// Gets the canvas the last committed frame was drawn on (after the disposal of the frame before it), kept for the
    /// formats that restore it or that write a frame once the next one is chosen; <see langword="null"/> otherwise.
    /// </summary>
    public AnimationCanvas? Canvas => _canvas;

    /// <summary>Starts the comparison of a frame with the canvases the disposal of the last committed frame can leave.</summary>
    /// <exception cref="InvalidOperationException">No frame was committed.</exception>
    public void BeginCompare()
    {
        var displayed = Displayed;
        if (CommittedFrames == 0)
            throw new InvalidOperationException("The first frame of an animation is committed without a comparison.");

        _chosen = -1;
        _enlarged = false;
        _candidateCount = 0;
        _candidates[_candidateCount++] = new Candidate(AnimationDisposal.None, displayed, Rectangle.Empty);
        _candidates[_candidateCount++] = new Candidate(AnimationDisposal.ClearToTransparent, displayed, _lastRegion);
        if (_rules.RestorePrevious && CommittedFrames > 1)
        {
            _candidates[_candidateCount++] = new Candidate(AnimationDisposal.RestorePrevious, _canvas!, Rectangle.Empty);
        }
    }

    /// <summary>Compares a row of the frame with the same row of each candidate canvas.</summary>
    /// <param name="y">The row.</param>
    /// <param name="row">The pixels of the frame.</param>
    public void CompareRow(int y, ReadOnlySpan<byte> row)
    {
        for (var i = 0; i < _candidateCount; i++)
        {
            CompareCandidateRow(ref _candidates[i], y, row);
        }
    }

    /// <summary>Chooses the disposal of the previous frame, the region and the blending of the compared frame.</summary>
    /// <param name="plan">The choice.</param>
    /// <returns>
    /// <see langword="true"/> once the frame is planned; <see langword="false"/> when the region of the previous frame grew
    /// (<see cref="AnimationDeltaRules.EnlargePreviousRegion"/>): every row of the frame must be compared again, then this
    /// method succeeds.
    /// </returns>
    public bool TryChoose(out AnimationFramePlan plan)
    {
        var best = -1;
        var bestArea = long.MaxValue;
        var bestOver = false;
        for (var i = 0; i < _candidateCount; i++)
        {
            ref readonly var candidate = ref _candidates[i];
            if (!_rules.SourceBlend && !candidate.OverExact)
                continue;

            var area = GetArea(GetRegion(in candidate));
            var over = OverBlend && candidate.OverExact;
            if (area < bestArea || (area == bestArea && over && !bestOver))
            {
                (best, bestArea, bestOver) = (i, area, over);
            }
        }

        if (best < 0)
        {
            if (!_rules.EnlargePreviousRegion || _enlarged)
                throw new InvalidOperationException("No disposal of the previous frame reproduces the frame.");

            // The pixels that become transparent are cleared by the disposal of the previous frame, whose region covers them
            var blocking = _candidates[0].GetBlockingRegion();
            var region = Rectangle.FromLTRB(Math.Min(_lastRegion.Left, blocking.Left), Math.Min(_lastRegion.Top, blocking.Top), Math.Max(_lastRegion.Right, blocking.Right), Math.Max(_lastRegion.Bottom, blocking.Bottom));
            _candidates[0] = new Candidate(AnimationDisposal.ClearToTransparent, Displayed, region);
            _candidateCount = 1;
            _enlarged = true;
            plan = default;
            return false;
        }

        ref readonly var chosen = ref _candidates[best];
        var frameRegion = GetRegion(in chosen);

        // OVER only when it changes something: a region whose pixels all differ is the same with SOURCE, which needs no
        // transparent value
        var blend = !_rules.SourceBlend || (bestOver && chosen.Changed < GetArea(frameRegion)) ? AnimationBlend.Over : AnimationBlend.Source;
        _chosen = best;
        _plan = new AnimationFramePlan(chosen.Disposal, chosen.Disposal == AnimationDisposal.ClearToTransparent ? chosen.Cleared : _lastRegion, frameRegion, blend);
        plan = _plan;
        return true;
    }

    /// <summary>Gets a row of the canvas the planned frame is drawn on (the first frame is drawn on a cleared canvas).</summary>
    /// <param name="y">The row.</param>
    /// <param name="clearedStart">The first column of the part of the row that is transparent black whatever the row holds.</param>
    /// <param name="clearedEnd">The column after that part (equal to <paramref name="clearedStart"/> when nothing is cleared).</param>
    /// <returns>The row, or an empty span when the whole row is transparent black.</returns>
    public ReadOnlySpan<byte> GetPlannedCanvasRow(int y, out int clearedStart, out int clearedEnd)
    {
        if (CommittedFrames == 0)
        {
            (clearedStart, clearedEnd) = (0, int.MaxValue);
            return default;
        }

        if (_chosen < 0)
            throw new InvalidOperationException("The frame is not planned.");

        return _candidates[_chosen].GetRow(y, out clearedStart, out clearedEnd);
    }

    /// <summary>Gets a value indicating whether a pixel of a frame equals the pixel of the canvas it is drawn on.</summary>
    /// <param name="row">The row of the frame.</param>
    /// <param name="x">The column.</param>
    /// <param name="canvasRow">The row of the canvas (<see cref="GetPlannedCanvasRow"/>).</param>
    /// <param name="clearedStart">The first column of the cleared part of the canvas row.</param>
    /// <param name="clearedEnd">The column after the cleared part of the canvas row.</param>
    /// <returns><see langword="true"/> when the pixel is unchanged.</returns>
    public bool IsUnchanged(ReadOnlySpan<byte> row, int x, ReadOnlySpan<byte> canvasRow, int clearedStart, int clearedEnd)
    {
        var offset = x * _bytesPerPixel;
        return x >= clearedStart && x < clearedEnd
            ? !row.Slice(offset, _bytesPerPixel).ContainsAnyExcept((byte)0)
            : PixelEquals(row, canvasRow, offset);
    }

    /// <summary>
    /// Applies the disposal of the plan (nothing for the first frame) to the private canvases, before the rows of the frame
    /// are copied (<see cref="CommitRow"/>).
    /// </summary>
    /// <param name="region">The region the frame is written with: the canvas for the first frame, the planned region otherwise.</param>
    public void BeginCommit(Rectangle region)
    {
        var displayed = Displayed;
        if (CommittedFrames > 0)
        {
            if (_chosen < 0)
                throw new InvalidOperationException("The frame is not planned.");

            // The canvas of this frame: the displayed frame (NONE), the displayed frame with the region of the previous
            // frame cleared (BACKGROUND), or the canvas of the previous frame (PREVIOUS), which stays as it is
            if (_canvas is { } canvas)
            {
                switch (_plan.PreviousDisposal)
                {
                    case AnimationDisposal.None:
                        (_displayed, _canvas) = (canvas, displayed);
                        break;

                    case AnimationDisposal.ClearToTransparent:
                        displayed.Clear(_plan.PreviousRegion);
                        (_displayed, _canvas) = (canvas, displayed);
                        break;
                }
            }
        }

        _lastRegion = region;
        _chosen = -1;
    }

    /// <summary>Copies a row of the frame, which becomes the displayed frame.</summary>
    /// <param name="y">The row.</param>
    /// <param name="row">The pixels of the frame.</param>
    public void CommitRow(int y, ReadOnlySpan<byte> row) => row.CopyTo(Displayed.GetRow(y));

    /// <summary>Ends the commit of a frame (every row was copied).</summary>
    public void EndCommit() => CommittedFrames++;

    public void Dispose()
    {
        _displayed?.Dispose();
        _displayed = null;
        _canvas?.Dispose();
        _canvas = null;
    }

    private static long GetArea(Rectangle region) => (long)region.Width * region.Height;

    private Rectangle GetRegion(ref readonly Candidate candidate)
    {
        var region = candidate.GetRegion();
        if (_rules.EvenOffsets && ((region.X | region.Y) & 1) != 0)
        {
            // The region grows to the left and to the top by unchanged pixels
            region = Rectangle.FromLTRB(region.Left & ~1, region.Top & ~1, region.Right, region.Bottom);
        }

        return region;
    }

    private void CompareCandidateRow(ref Candidate candidate, int y, ReadOnlySpan<byte> row)
    {
        var reference = candidate.GetRow(y, out var clearedStart, out var clearedEnd);
        var width = row.Length / _bytesPerPixel;
        clearedStart = Math.Min(clearedStart, width);
        clearedEnd = Math.Min(clearedEnd, width);
        CompareSegment(ref candidate, y, row, reference, 0, clearedStart);
        CompareCleared(ref candidate, y, row, clearedStart, clearedEnd);
        CompareSegment(ref candidate, y, row, reference, clearedEnd, width);
    }

    /// <summary>Compares the pixels <c>[start, end)</c> of a row with a canvas row.</summary>
    private void CompareSegment(ref Candidate candidate, int y, ReadOnlySpan<byte> row, ReadOnlySpan<byte> reference, int start, int end)
    {
        if (start >= end)
            return;

        var bytes = row[(start * _bytesPerPixel)..(end * _bytesPerPixel)];
        var prefix = bytes.CommonPrefixLength(reference[(start * _bytesPerPixel)..(end * _bytesPerPixel)]);
        if (prefix == bytes.Length)
            return;

        var first = start + (prefix / _bytesPerPixel);
        var last = end - 1;
        while (last > first && PixelEquals(row, reference, last * _bytesPerPixel))
        {
            last--;
        }

        for (var x = first; x <= last; x++)
        {
            var offset = x * _bytesPerPixel;
            if (PixelEquals(row, reference, offset))
                continue;

            Include(ref candidate, x, y, row.Slice(offset, _bytesPerPixel), canvasTransparent: !reference.Slice(offset + _alphaOffset, _alphaBytes).ContainsAnyExcept((byte)0));
        }
    }

    /// <summary>Compares the pixels <c>[start, end)</c> of a row with transparent black.</summary>
    private void CompareCleared(ref Candidate candidate, int y, ReadOnlySpan<byte> row, int start, int end)
    {
        if (start >= end)
            return;

        var bytes = row[(start * _bytesPerPixel)..(end * _bytesPerPixel)];
        var firstByte = bytes.IndexOfAnyExcept((byte)0);
        if (firstByte < 0)
            return;

        var first = start + (firstByte / _bytesPerPixel);
        var last = start + (bytes.LastIndexOfAnyExcept((byte)0) / _bytesPerPixel);
        for (var x = first; x <= last; x++)
        {
            var pixel = row.Slice(x * _bytesPerPixel, _bytesPerPixel);
            if (pixel.ContainsAnyExcept((byte)0))
            {
                Include(ref candidate, x, y, pixel, canvasTransparent: true);
            }
        }
    }

    private void Include(ref Candidate candidate, int x, int y, ReadOnlySpan<byte> pixel, bool canvasTransparent)
    {
        candidate.Include(x, y);
        var alpha = pixel.Slice(_alphaOffset, _alphaBytes);
        var opaque = !alpha.ContainsAnyExcept(byte.MaxValue);
        if (opaque || (_rules.TranslucentOverTransparent && canvasTransparent && alpha.ContainsAnyExcept((byte)0)))
            return;

        candidate.IncludeBlocking(x, y);
    }

    private bool PixelEquals(ReadOnlySpan<byte> row, ReadOnlySpan<byte> reference, int offset) => _bytesPerPixel == 4
        ? BinaryPrimitives.ReadUInt32LittleEndian(row[offset..]) == BinaryPrimitives.ReadUInt32LittleEndian(reference[offset..])
        : BinaryPrimitives.ReadUInt64LittleEndian(row[offset..]) == BinaryPrimitives.ReadUInt64LittleEndian(reference[offset..]);

    /// <summary>The comparison of a frame with one canvas its previous frame can leave.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Candidate(AnimationDisposal disposal, AnimationCanvas reference, Rectangle cleared)
    {
        private int _left = int.MaxValue;
        private int _top = int.MaxValue;
        private int _right = -1;
        private int _bottom = -1;
        private int _blockingLeft = int.MaxValue;
        private int _blockingTop = int.MaxValue;
        private int _blockingRight = -1;
        private int _blockingBottom = -1;

        public AnimationDisposal Disposal { get; } = disposal;

        /// <summary>Gets the rectangle that is transparent black on the canvas whatever the reference holds.</summary>
        public Rectangle Cleared { get; } = cleared;

        /// <summary>Gets the number of pixels that differ.</summary>
        public long Changed { get; private set; }

        /// <summary>Gets a value indicating whether OVER gives every pixel that differs exactly.</summary>
        public readonly bool OverExact => _blockingRight < 0;

        public readonly ReadOnlySpan<byte> GetRow(int y, out int clearedStart, out int clearedEnd)
        {
            (clearedStart, clearedEnd) = y >= Cleared.Top && y < Cleared.Bottom ? (Cleared.Left, Cleared.Right) : (0, 0);
            return reference.GetRow(y);
        }

        public void Include(int x, int y)
        {
            _left = Math.Min(_left, x);
            _top = Math.Min(_top, y);
            _right = Math.Max(_right, x);
            _bottom = Math.Max(_bottom, y);
            Changed++;
        }

        public void IncludeBlocking(int x, int y)
        {
            _blockingLeft = Math.Min(_blockingLeft, x);
            _blockingTop = Math.Min(_blockingTop, y);
            _blockingRight = Math.Max(_blockingRight, x);
            _blockingBottom = Math.Max(_blockingBottom, y);
        }

        /// <summary>Gets the bounding box of the pixels that differ, or a 1x1 region when none does.</summary>
        public readonly Rectangle GetRegion() => _right < 0 ? new Rectangle(0, 0, 1, 1) : Rectangle.FromLTRB(_left, _top, _right + 1, _bottom + 1);

        /// <summary>Gets the bounding box of the pixels that differ and that OVER cannot give exactly.</summary>
        public readonly Rectangle GetBlockingRegion() => _blockingRight < 0 ? Rectangle.Empty : Rectangle.FromLTRB(_blockingLeft, _blockingTop, _blockingRight + 1, _blockingBottom + 1);
    }
}
