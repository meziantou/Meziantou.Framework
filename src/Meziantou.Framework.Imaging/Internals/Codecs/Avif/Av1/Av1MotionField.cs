namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The motion field of an AV1 frame (AV1 specification, sections 7.9 "Motion field estimation process" and 7.19 "Motion field
/// motion vector storage process"): the motion vectors that a frame saves for the frames that refer to it, and their
/// projection to the frame being decoded. Both are stored for the 8x8 luma blocks of the frame, which are the only positions
/// that the decoding process reads: <c>MiCols &gt;&gt; 1</c> entries per row.
/// </summary>
internal static class Av1MotionField
{
    private const short InvalidMv = short.MinValue;

    /// <summary>
    /// Stores the motion vectors of the frame that refer to a frame output before it (<c>MfRefFrames</c> and <c>MfMvs</c>).
    /// </summary>
    /// <param name="sequence">The sequence header.</param>
    /// <param name="header">The header of the frame.</param>
    /// <param name="refFrames"><c>RefFrames</c>: 2 entries per 4x4 luma block.</param>
    /// <param name="mvs"><c>Mvs</c>: 4 entries per 4x4 luma block.</param>
    /// <param name="savedRefFrames">Receives the reference frame of each 8x8 luma block, or -1.</param>
    /// <param name="savedMvs">Receives the row and the column of the motion vector of each 8x8 luma block.</param>
    public static void Store(Av1SequenceHeader sequence, Av1FrameHeader header, ReadOnlySpan<sbyte> refFrames, ReadOnlySpan<short> mvs, Span<sbyte> savedRefFrames, Span<short> savedMvs)
    {
        var w8 = header.MiCols >> 1;
        var h8 = header.MiRows >> 1;
        Span<bool> isPast = stackalloc bool[Av1Constants.TotalRefsPerFrame];
        for (var reference = Av1Constants.LastFrame; reference <= Av1Constants.AltrefFrame; reference++)
        {
            isPast[reference] = Av1FrameHeaderParser.GetRelativeDistance(sequence, header.OrderHints[reference], header.OrderHint) < 0;
        }

        for (var y8 = 0; y8 < h8; y8++)
        {
            for (var x8 = 0; x8 < w8; x8++)
            {
                var source = (((2 * y8) + 1) * header.MiCols) + (2 * x8) + 1;
                var destination = (y8 * w8) + x8;
                sbyte savedRef = -1;
                short savedRow = 0;
                short savedCol = 0;
                for (var list = 0; list < 2; list++)
                {
                    var r = refFrames[(source * 2) + list];
                    if (r <= Av1Constants.IntraFrame || !isPast[r])
                        continue;

                    var mvRow = mvs[(source * 4) + (list * 2)];
                    var mvCol = mvs[(source * 4) + (list * 2) + 1];
                    if (Math.Abs(mvRow) <= Av1Constants.RefmvsLimit && Math.Abs(mvCol) <= Av1Constants.RefmvsLimit)
                    {
                        savedRef = r;
                        savedRow = mvRow;
                        savedCol = mvCol;
                    }
                }

                savedRefFrames[destination] = savedRef;
                savedMvs[destination * 2] = savedRow;
                savedMvs[(destination * 2) + 1] = savedCol;
            }
        }
    }

    /// <summary>
    /// Projects the motion vectors saved by the reference frames to the frame being decoded (<c>MotionFieldMvs</c>).
    /// </summary>
    /// <param name="sequence">The sequence header.</param>
    /// <param name="header">The header of the frame being decoded.</param>
    /// <param name="references">The reference frames of the frame being decoded.</param>
    /// <param name="motionFieldMvs">
    /// Receives, for each reference frame type from <c>LAST_FRAME</c>, the row and the column of the motion vector of each 8x8
    /// luma block; the row is <see cref="short.MinValue"/> for a block without motion vector.
    /// </param>
    public static void Estimate(Av1SequenceHeader sequence, Av1FrameHeader header, Av1ReferenceFrames references, Span<short> motionFieldMvs)
    {
        motionFieldMvs.Fill(InvalidMv);
        var lastAltOrderHint = references.Get(Av1Constants.LastFrame).Header.OrderHints[Av1Constants.AltrefFrame];
        var curGoldOrderHint = header.OrderHints[Av1Constants.GoldenFrame];
        if (lastAltOrderHint != curGoldOrderHint)
            Project(sequence, header, references, motionFieldMvs, Av1Constants.LastFrame, dstSign: -1);

        var refStamp = Av1Constants.MfmvStackSize - 2;
        if (IsFuture(Av1Constants.BwdrefFrame) && Project(sequence, header, references, motionFieldMvs, Av1Constants.BwdrefFrame, dstSign: 1))
            refStamp--;

        if (IsFuture(Av1Constants.Altref2Frame) && Project(sequence, header, references, motionFieldMvs, Av1Constants.Altref2Frame, dstSign: 1))
            refStamp--;

        if (IsFuture(Av1Constants.AltrefFrame) && refStamp >= 0 && Project(sequence, header, references, motionFieldMvs, Av1Constants.AltrefFrame, dstSign: 1))
            refStamp--;

        if (refStamp >= 0)
            Project(sequence, header, references, motionFieldMvs, Av1Constants.Last2Frame, dstSign: -1);

        bool IsFuture(int refFrame) => Av1FrameHeaderParser.GetRelativeDistance(sequence, header.OrderHints[refFrame], header.OrderHint) > 0;
    }

    // Section 7.9.2 "Projection process"
    private static bool Project(Av1SequenceHeader sequence, Av1FrameHeader header, Av1ReferenceFrames references, Span<short> motionFieldMvs, int src, int dstSign)
    {
        var source = references.Get(src);
        var sourceHeader = source.Header;
        if (sourceHeader.MiRows != header.MiRows || sourceHeader.MiCols != header.MiCols || sourceHeader.FrameType is Av1Constants.IntraOnlyFrame or Av1Constants.KeyFrame)
            return false;

        var savedRefFrames = source.MotionFieldRefFrames;
        var savedMvs = source.MotionFieldMvs;
        if (savedRefFrames.IsEmpty)
            return true;

        var w8 = header.MiCols >> 1;
        var h8 = header.MiRows >> 1;
        var fieldLength = w8 * h8 * 2;
        var refToCur = Av1FrameHeaderParser.GetRelativeDistance(sequence, header.OrderHints[src], header.OrderHint);
        Span<int> refToDst = stackalloc int[Av1Constants.TotalRefsPerFrame];
        for (var dst = Av1Constants.LastFrame; dst <= Av1Constants.AltrefFrame; dst++)
        {
            refToDst[dst] = Av1FrameHeaderParser.GetRelativeDistance(sequence, header.OrderHint, header.OrderHints[dst]);
        }

        for (var y8 = 0; y8 < h8; y8++)
        {
            for (var x8 = 0; x8 < w8; x8++)
            {
                var index = (y8 * w8) + x8;
                var srcRef = savedRefFrames[index];
                if (srcRef <= Av1Constants.IntraFrame)
                    continue;

                var refOffset = Av1FrameHeaderParser.GetRelativeDistance(sequence, header.OrderHints[src], sourceHeader.OrderHints[srcRef]);
                if (Math.Abs(refToCur) > Av1Constants.MaxFrameDistance || Math.Abs(refOffset) > Av1Constants.MaxFrameDistance || refOffset <= 0)
                    continue;

                int mvRow = savedMvs[index * 2];
                int mvCol = savedMvs[(index * 2) + 1];
                var projRow = GetMvProjection(mvRow, refToCur * dstSign, refOffset);
                var projCol = GetMvProjection(mvCol, refToCur * dstSign, refOffset);

                // Section 7.9.4 "Get block position process"
                if (!TryProject(y8, projRow, dstSign, h8, Av1Constants.MaxOffsetHeight, out var posY8) || !TryProject(x8, projCol, dstSign, w8, Av1Constants.MaxOffsetWidth, out var posX8))
                    continue;

                var position = ((posY8 * w8) + posX8) * 2;
                for (var dst = Av1Constants.LastFrame; dst <= Av1Constants.AltrefFrame; dst++)
                {
                    var field = motionFieldMvs.Slice((dst - Av1Constants.LastFrame) * fieldLength, fieldLength);
                    field[position] = (short)GetMvProjection(mvRow, refToDst[dst], refOffset);
                    field[position + 1] = (short)GetMvProjection(mvCol, refToDst[dst], refOffset);
                }
            }
        }

        return true;
    }

    // Section 7.9.3 "Get MV projection process"
    private static int GetMvProjection(int mv, int numerator, int denominator)
    {
        var clippedDenominator = Math.Min(Av1Constants.MaxFrameDistance, denominator);
        var clippedNumerator = Math.Clamp(numerator, -Av1Constants.MaxFrameDistance, Av1Constants.MaxFrameDistance);
        var product = mv * clippedNumerator * Av1Tables.DivMult[clippedDenominator];
        var scaled = product >= 0 ? (product + (1 << 13)) >> 14 : -((-product + (1 << 13)) >> 14);
        return Math.Clamp(scaled, -(1 << 14) + 1, (1 << 14) - 1);
    }

    // The project function of section 7.9.4 "Get block position process"
    private static bool TryProject(int v8, int delta, int dstSign, int max8, int maxOff8, out int position)
    {
        var base8 = (v8 >> 3) << 3;
        var offset8 = delta >= 0 ? delta >> (3 + 1 + Av1Constants.MiSizeLog2) : -((-delta) >> (3 + 1 + Av1Constants.MiSizeLog2));
        position = v8 + (dstSign * offset8);
        return position >= 0 && position < max8 && position >= base8 - maxOff8 && position < base8 + 8 + maxOff8;
    }
}
