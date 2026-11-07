using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The search of intra block copy (<c>use_intrabc</c>, AV1 specification, sections 5.11.7 and 7.11.3.2): a block of an
/// intra frame that is predicted from a part of the frame that is already coded, at a whole number of samples.
/// </content>
/// <remarks>
/// <para>
/// The frame is lossless, so its coded samples are its source samples. The blocks of 8x8 luma samples that a block may
/// copy are in a table of the workspace, by the hash of their samples: every position of the superblocks that are at
/// least five superblocks behind the one being coded, in the order of the tile, which is the delay that
/// <see cref="IsMvValid"/> requires. Flat blocks are left out: an intra mode predicts them, and they would make long
/// lists.
/// </para>
/// <para>
/// A block looks up its first 8x8 cell that is not flat, and keeps the positions at which the whole block is the same in
/// every plane, among the ones a decoder accepts. The one whose motion vector is the closest to the predicted one is
/// encoded like an inter block, and compared with the intra modes.
/// </para>
/// </remarks>
internal ref partial struct Av1TileCoder
{
    private const int BlockCopyCell = 8;

    // The positions that are examined for a block, and the matches among which the cheapest one is kept
    private const int MaxBlockCopyProbes = 256;
    private const int MaxBlockCopyMatches = 8;

    // Adds the blocks of the superblocks that the superblock at (r, c) may copy and that are not in the table yet
    private readonly void AddBlockCopySources(int r, int c)
    {
        Debug.Assert(!_sequence.Use128x128Superblock);
        var workspace = _workspace!;
        var perRow = ((_miColEnd - _miColStart - 1) >> 4) + 1;
        var active = (((r - _miRowStart) >> 4) * perRow) + ((c - _miColStart) >> 4);
        var limit = active - Av1Constants.IntrabcDelaySb64;
        var plane = workspace.GetSource(0);
        var samples = plane.Samples;
        var stride = plane.Stride;
        var heads = workspace.BlockCopyHeads;
        var next = workspace.BlockCopyNext;
        var tileLeft = _miColStart * Av1Constants.MiSize;
        var tileTop = _miRowStart * Av1Constants.MiSize;
        var tileRight = Math.Min(_miColEnd * Av1Constants.MiSize, plane.Width);
        var tileBottom = Math.Min(_miRowEnd * Av1Constants.MiSize, plane.Height);
        for (var superblock = workspace.BlockCopySuperblocks; superblock < limit; superblock++)
        {
            // The blocks whose last sample is in the superblock: the ones that are whole when it is coded
            var left = tileLeft + ((superblock % perRow) << 6);
            var top = tileTop + ((superblock / perRow) << 6);
            var firstX = Math.Max(tileLeft, left - (BlockCopyCell - 1));
            var firstY = Math.Max(tileTop, top - (BlockCopyCell - 1));
            var lastX = Math.Min(left + 64, tileRight) - BlockCopyCell;
            var lastY = Math.Min(top + 64, tileBottom) - BlockCopyCell;
            for (var y = firstY; y <= lastY; y++)
            {
                for (var x = firstX; x <= lastX; x++)
                {
                    if (!TryGetBlockCopyHash(samples, stride, x, y, out var hash))
                        continue;

                    var position = (y * plane.Width) + x;
                    next[position] = heads[hash];
                    heads[hash] = position;
                }
            }
        }

        workspace.BlockCopySuperblocks = Math.Max(workspace.BlockCopySuperblocks, limit);
    }

    // The hash of a block of 8x8 luma samples; false for a block of one value
    private static bool TryGetBlockCopyHash(ReadOnlySpan<ushort> samples, int stride, int x, int y, out int hash)
    {
        var first = samples[(y * stride) + x];
        var isFlat = true;
        var value = 2166136261u;
        for (var i = 0; i < BlockCopyCell; i++)
        {
            foreach (var sample in samples.Slice(((y + i) * stride) + x, BlockCopyCell))
            {
                isFlat &= sample == first;
                value = (value ^ sample) * 16777619u;
            }
        }

        hash = (int)((value ^ (value >> 16)) & (Av1EncoderWorkspace.BlockCopyBuckets - 1));
        return !isFlat;
    }

    // The search of the block as a copy of a part of the frame: the block is encoded with the best motion vector, if any
    private void SearchBlockCopy(ref InterSearch search)
    {
        var bw = search.Bw4 * Av1Constants.MiSize;
        var bh = search.Bh4 * Av1Constants.MiSize;
        if (bw < BlockCopyCell || bh < BlockCopyCell)
            return;

        // The motion vector that the stack of the block predicts: the cheapest one to code
        SetCopyBlockDefaults();
        FindMvStack(isCompound: false);
        SetIntrabcPredMv();
        if (!FindBlockCopy(bw, bh, _predMv[0], _predMv[1], out var mvRow, out var mvCol))
            return;

        var triedCount = 0;
        TryInterMode(ref search, Av1Constants.DcPred, refMvIdx: 0, mvRow, mvCol, tried: default, ref triedCount);
    }

    // What the cells of a block are: none of them is a copy, some are, most are (three quarters of the ones that are not
    // flat), or the block as a whole is a copy
    private enum BlockCopyState
    {
        None,
        Some,
        Most,
        Whole,
    }

    // Looks up each cell of 8x8 samples of the superblock at (r, c): the search of a partition then knows which blocks
    // are worth splitting down to the cells that are copies
    private void FindBlockCopyCells(int r, int c, int sbSize4)
    {
        var cells = _workspace!.BlockCopyCells;
        cells.Clear();
        var plane = _workspace.GetSource(0);
        var (miRow, miCol, miSize, hasChroma, mv) = (_miRow, _miCol, _miSize, _hasChroma, _mv);
        var useIntrabc = _useIntrabc;
        _miSize = Av1Constants.Block8x8;
        _hasChroma = _sequence.PlaneCount > 1;
        _useIntrabc = true;
        for (var row = 0; row < sbSize4; row += 2)
        {
            for (var col = 0; col < sbSize4; col += 2)
            {
                _miRow = r + row;
                _miCol = c + col;
                var x = _miCol * Av1Constants.MiSize;
                var y = _miRow * Av1Constants.MiSize;
                if (x + BlockCopyCell > plane.Width || y + BlockCopyCell > plane.Height || !TryGetBlockCopyHash(plane.Samples, plane.Stride, x, y, out _))
                    continue;

                cells[((row >> 1) * 16) + (col >> 1)] = (byte)(FindBlockCopy(BlockCopyCell, BlockCopyCell, predRow: 0, predCol: 0, out _, out _) ? 2 : 1);
            }
        }

        (_miRow, _miCol, _miSize, _hasChroma, _mv) = (miRow, miCol, miSize, hasChroma, mv);
        _useIntrabc = useIntrabc;
    }

    // What the cells of the block of bSize at (r, c) are, from the cells of its superblock
    private BlockCopyState GetBlockCopyState(int r, int c, int bSize)
    {
        var cells = _workspace!.BlockCopyCells;
        var bw4 = Av1Tables.Num4x4BlocksWide[bSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[bSize];
        var count = 0;
        var copies = 0;
        for (var row = (r & _sbMask) >> 1; row < ((r & _sbMask) + bh4) >> 1; row++)
        {
            foreach (var cell in cells.Slice((row * 16) + ((c & _sbMask) >> 1), bw4 >> 1))
            {
                count += cell is 0 ? 0 : 1;
                copies += cell is 2 ? 1 : 0;
            }
        }

        if (copies is 0)
            return BlockCopyState.None;

        if (copies < count)
            return copies * 4 >= count * 3 ? BlockCopyState.Most : BlockCopyState.Some;

        // Every cell is a copy: the block is one when the same part of the frame has them all
        var (miRow, miCol, miSize, hasChroma, mv) = (_miRow, _miCol, _miSize, _hasChroma, _mv);
        var useIntrabc = _useIntrabc;
        (_miRow, _miCol, _miSize) = (r, c, bSize);
        _hasChroma = _sequence.PlaneCount > 1;
        _useIntrabc = true;
        var isWhole = FindBlockCopy(bw4 * Av1Constants.MiSize, bh4 * Av1Constants.MiSize, predRow: 0, predCol: 0, out _, out _);
        (_miRow, _miCol, _miSize, _hasChroma, _mv) = (miRow, miCol, miSize, hasChroma, mv);
        _useIntrabc = useIntrabc;
        return isWhole ? BlockCopyState.Whole : BlockCopyState.Most;
    }

    // Looks for a part of the frame that a decoder lets the block copy and that is the block, in every plane
    private bool FindBlockCopy(int bw, int bh, int predRow, int predCol, out int mvRow, out int mvCol)
    {
        mvRow = 0;
        mvCol = 0;
        var workspace = _workspace!;
        var plane = workspace.GetSource(0);
        var x0 = _miCol * Av1Constants.MiSize;
        var y0 = _miRow * Av1Constants.MiSize;
        if (x0 + bw > plane.Width || y0 + bh > plane.Height)
            return false;

        // The first cell of the block that is in the table
        var samples = plane.Samples;
        var stride = plane.Stride;
        var hash = 0;
        var cellX = -1;
        var cellY = 0;
        for (var y = 0; y < bh && cellX < 0; y += BlockCopyCell)
        {
            for (var x = 0; x < bw; x += BlockCopyCell)
            {
                if (TryGetBlockCopyHash(samples, stride, x0 + x, y0 + y, out hash))
                {
                    (cellX, cellY) = (x, y);
                    break;
                }
            }
        }

        if (cellX < 0)
            return false;

        var next = workspace.BlockCopyNext;
        var best = long.MaxValue;
        var matches = 0;
        var probes = 0;
        for (var position = workspace.BlockCopyHeads[hash]; position >= 0 && probes < MaxBlockCopyProbes && matches < MaxBlockCopyMatches; position = next[position])
        {
            probes++;
            var sourceX = (position % plane.Width) - cellX;
            var sourceY = (position / plane.Width) - cellY;
            if (sourceX < 0 || sourceY < 0 || sourceX + bw > plane.Width || sourceY + bh > plane.Height)
                continue;

            _mv[0] = (sourceY - y0) * 8;
            _mv[1] = (sourceX - x0) * 8;
            if (!IsMvValid(isCompound: false) || !IsSameBlock(x0, y0, sourceX, sourceY, bw, bh))
                continue;

            matches++;
            var distance = (long)Math.Abs(_mv[0] - predRow) + Math.Abs(_mv[1] - predCol);
            if (distance < best)
                (best, mvRow, mvCol) = (distance, _mv[0], _mv[1]);
        }

        return matches > 0;
    }

    // Whether the samples of two blocks of the frame to encode are the same in every plane. The chroma samples of a
    // subsampled plane are only copied at whole samples when the luma samples move by an even number of them
    private readonly bool IsSameBlock(int x0, int y0, int sourceX, int sourceY, int bw, int bh)
    {
        var workspace = _workspace!;
        for (var plane = 0; plane < 1 + (_hasChroma ? 2 : 0); plane++)
        {
            var subX = plane > 0 ? _subX : 0;
            var subY = plane > 0 ? _subY : 0;
            if ((((sourceX - x0) & subX) | ((sourceY - y0) & subY)) is not 0)
                return false;

            var buffer = workspace.GetSource(plane);
            var samples = buffer.Samples;
            var stride = buffer.Stride;
            var width = bw >> subX;
            for (var y = 0; y < bh >> subY; y++)
            {
                var block = samples.Slice((((y0 >> subY) + y) * stride) + (x0 >> subX), width);
                var source = samples.Slice((((sourceY >> subY) + y) * stride) + (sourceX >> subX), width);
                if (!block.SequenceEqual(source))
                    return false;
            }
        }

        return true;
    }
}
