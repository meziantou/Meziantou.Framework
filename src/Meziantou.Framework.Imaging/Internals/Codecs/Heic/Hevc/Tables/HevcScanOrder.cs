namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The scan orders of the transform coefficients, <c>ScanOrder[ log2BlockSize ][ scanIdx ][ sPos ][ sComp ]</c>, computed by
/// the initialization processes of Rec. ITU-T H.265, clauses 6.5.3 (up-right diagonal, <c>scanIdx</c> 0), 6.5.4
/// (horizontal, <c>scanIdx</c> 1) and 6.5.5 (vertical, <c>scanIdx</c> 2), for the blocks of 1x1 to 8x8 positions that the
/// residual coding syntax and the scaling lists use. A position is one byte: the horizontal component in its four lower
/// bits and the vertical component in its four upper bits.
/// </summary>
internal static class HevcScanOrder
{
    public const int Diagonal = 0;
    public const int Horizontal = 1;
    public const int Vertical = 2;

    private const int MaxLog2BlockSize = 3;

    private static readonly byte[][] Orders = Create();

    /// <summary>Gets the positions of the scan <paramref name="scanIdx"/> of a block of <c>1 &lt;&lt; log2BlockSize</c> positions on a side.</summary>
    public static ReadOnlySpan<byte> Get(int log2BlockSize, int scanIdx) => Orders[(log2BlockSize * 3) + scanIdx];

    /// <summary>Gets the horizontal component of a position.</summary>
    public static int X(byte position) => position & 15;

    /// <summary>Gets the vertical component of a position.</summary>
    public static int Y(byte position) => position >> 4;

    private static byte[][] Create()
    {
        var orders = new byte[(MaxLog2BlockSize + 1) * 3][];
        for (var log2BlockSize = 0; log2BlockSize <= MaxLog2BlockSize; log2BlockSize++)
        {
            var blkSize = 1 << log2BlockSize;
            orders[(log2BlockSize * 3) + Diagonal] = CreateDiagonal(blkSize);
            orders[(log2BlockSize * 3) + Horizontal] = CreateHorizontal(blkSize);
            orders[(log2BlockSize * 3) + Vertical] = CreateVertical(blkSize);
        }

        return orders;
    }

    // Clause 6.5.3 "Up-right diagonal scan order array initialization process"
    private static byte[] CreateDiagonal(int blkSize)
    {
        var diagScan = new byte[blkSize * blkSize];
        var i = 0;
        var x = 0;
        var y = 0;
        var stopLoop = false;
        while (!stopLoop)
        {
            while (y >= 0)
            {
                if (x < blkSize && y < blkSize)
                {
                    diagScan[i] = (byte)((y << 4) | x);
                    i++;
                }

                y--;
                x++;
            }

            y = x;
            x = 0;
            if (i >= blkSize * blkSize)
            {
                stopLoop = true;
            }
        }

        return diagScan;
    }

    // Clause 6.5.4 "Horizontal scan order array initialization process"
    private static byte[] CreateHorizontal(int blkSize)
    {
        var horScan = new byte[blkSize * blkSize];
        var i = 0;
        for (var y = 0; y < blkSize; y++)
        {
            for (var x = 0; x < blkSize; x++)
            {
                horScan[i] = (byte)((y << 4) | x);
                i++;
            }
        }

        return horScan;
    }

    // Clause 6.5.5 "Vertical scan order array initialization process"
    private static byte[] CreateVertical(int blkSize)
    {
        var verScan = new byte[blkSize * blkSize];
        var i = 0;
        for (var x = 0; x < blkSize; x++)
        {
            for (var y = 0; y < blkSize; y++)
            {
                verScan[i] = (byte)((y << 4) | x);
                i++;
            }
        }

        return verScan;
    }
}
