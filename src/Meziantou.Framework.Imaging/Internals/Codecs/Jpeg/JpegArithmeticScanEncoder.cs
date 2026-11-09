namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coding procedures of the scans of an arithmetic-coded DCT frame (ITU-T T.81 sections F.1.4 and G.1.3, frames SOF9
/// and SOF10): the DC differences and the AC coefficients of a block as binary decisions, each with the statistics bin its
/// context selects. It is the inverse of <see cref="JpegArithmeticSequentialScanDecoder"/> and
/// <see cref="JpegArithmeticProgressiveScanDecoder"/>, and follows libjpeg (<c>jcarith.c</c>).
/// </summary>
/// <remarks>
/// <para>
/// The luminance component uses the conditioning tables 0, the other components the tables 1, with the default
/// conditioning of T.81 F.1.4.4.1.4 and F.1.4.4.2.1 (<c>L = 0</c>, <c>U = 1</c>, <c>Kx = 5</c>), so no DAC segment is
/// written.
/// </para>
/// <para>
/// The statistics, the DC predictions and the classes of the previous DC differences start from zero at the start of each
/// scan and after each restart marker (T.81 F.1.4.4). The blocks are in zig-zag order.
/// </para>
/// </remarks>
internal sealed class JpegArithmeticScanEncoder
{
    /// <summary>The upper bound of the DC conditioning (T.81 F.1.4.4.1.4: the default of 1).</summary>
    private const int DcUpper = 1;

    /// <summary>The last coefficient that uses the first set of magnitude bins (T.81 F.1.4.4.2.1: the default of 5).</summary>
    private const int AcThreshold = 5;

    // Table F.4: the first magnitude category bin of a DC table; table F.5: those of the AC coefficients up to Kx, and of the others
    private const int DcCategoryBins = 20;
    private const int AcLowCategoryBins = 189;
    private const int AcHighCategoryBins = 217;
    private const int BitBinsOffset = 14;

    private readonly byte[][] _dcStatistics = [new byte[JpegArithmeticConditioning.DcBinCount], new byte[JpegArithmeticConditioning.DcBinCount]];
    private readonly byte[][] _acStatistics = [new byte[JpegArithmeticConditioning.AcBinCount], new byte[JpegArithmeticConditioning.AcBinCount]];
    private readonly int[] _predictions = new int[3];
    private readonly int[] _dcContexts = new int[3];

    /// <summary>Gets the arithmetic encoder, whose output the caller copies.</summary>
    public JpegArithmeticEncoder Encoder { get; } = new();

    /// <summary>Gets the index of the conditioning tables of a component.</summary>
    public static int GetTableIndex(int component) => component == 0 ? 0 : 1;

    /// <summary>Starts a scan: the statistics, the predictions and the encoder start from zero.</summary>
    public void BeginScan()
    {
        ResetStatistics();
        Encoder.Reset();
    }

    /// <summary>Ends a restart interval: the segment is flushed, the RSTn marker written, and everything starts from zero (T.81 F.1.4.4).</summary>
    /// <param name="number">The number of the marker, 0 to 7.</param>
    public void Restart(int number)
    {
        Encoder.Flush();
        Encoder.WriteMarker((byte)(0xD0 + number));
        ResetStatistics();
    }

    /// <summary>Ends the scan: the last segment is flushed.</summary>
    public void EndScan() => Encoder.Flush();

    /// <summary>Codes a block of a sequential scan (T.81 F.1.4): the DC difference, then the AC coefficients.</summary>
    /// <param name="component">The index of the component of the block.</param>
    /// <param name="block">The quantized coefficients, in zig-zag order.</param>
    public void EncodeSequentialBlock(int component, ReadOnlySpan<short> block)
    {
        var table = GetTableIndex(component);
        EncodeDcDifference(component, block[0] - _predictions[component]);
        _predictions[component] = block[0];
        EncodeAcFirst(table, block, start: 1, end: 63, low: 0);
    }

    /// <summary>Codes a block of a DC first scan (T.81 G.1.3.1): the difference of the DC values shifted right by <paramref name="low"/>.</summary>
    public void EncodeDcFirst(int component, ReadOnlySpan<short> block, int low)
    {
        var value = block[0] >> low;
        EncodeDcDifference(component, value - _predictions[component]);
        _predictions[component] = value;
    }

    /// <summary>Codes a block of a DC refinement scan (T.81 G.1.3.1): the bit <paramref name="low"/> of the DC value, with the fixed estimate.</summary>
    public void EncodeDcRefinement(ReadOnlySpan<short> block, int low) => Encoder.EncodeFixed((block[0] >> low) & 1);

    /// <summary>
    /// Codes the coefficients of a spectral band of a block (T.81 F.1.4.2, and G.1.3.2 for an AC first scan): the magnitudes
    /// divided by <c>2^low</c> (rounded toward zero).
    /// </summary>
    /// <param name="table">The index of the AC conditioning table.</param>
    /// <param name="block">The quantized coefficients, in zig-zag order.</param>
    /// <param name="start">The first coefficient of the band, at least 1.</param>
    /// <param name="end">The last coefficient of the band.</param>
    /// <param name="low">The bit position of the scan (<c>Al</c>).</param>
    public void EncodeAcFirst(int table, ReadOnlySpan<short> block, int start, int end, int low)
    {
        var statistics = _acStatistics[table];
        var encoder = Encoder;

        // The last coefficient of the band that is not zero once divided
        var last = end;
        while (last >= start && Math.Abs((int)block[last]) >> low == 0)
        {
            last--;
        }

        var k = start;
        for (; k <= last; k++)
        {
            var bin = 3 * (k - 1);
            encoder.Encode(ref statistics[bin], 0);
            int value;
            while ((value = block[k] < 0 ? -(-(int)block[k] >> low) : block[k] >> low) == 0)
            {
                encoder.Encode(ref statistics[bin + 1], 0);
                bin += 3;
                k++;
            }

            encoder.Encode(ref statistics[bin + 1], 1);
            encoder.EncodeFixed(value < 0 ? 1 : 0);
            EncodeAcMagnitude(statistics, bin + 2, Math.Abs(value) - 1, k);
        }

        // The end of the block, unless its last coefficient is the last of the band
        if (k <= end)
        {
            encoder.Encode(ref statistics[3 * (k - 1)], 1);
        }
    }

    /// <summary>
    /// Codes a block of an AC refinement scan (T.81 G.1.3.3): the bit <paramref name="low"/> of the coefficients of the band
    /// that are already nonzero at the bit <c>low + 1</c>, and the coefficients that become nonzero, with their sign.
    /// </summary>
    public void EncodeAcRefinement(int table, ReadOnlySpan<short> block, int start, int end, int low)
    {
        var statistics = _acStatistics[table];
        var encoder = Encoder;

        // The last coefficient nonzero at this bit, and the last one the previous scans made nonzero
        var last = end;
        while (last >= start && Math.Abs((int)block[last]) >> low == 0)
        {
            last--;
        }

        var previousLast = last;
        while (previousLast >= start && Math.Abs((int)block[previousLast]) >> (low + 1) == 0)
        {
            previousLast--;
        }

        var k = start;
        for (; k <= last; k++)
        {
            var bin = 3 * (k - 1);
            if (k > previousLast)
            {
                encoder.Encode(ref statistics[bin], 0);
            }

            while (true)
            {
                var magnitude = Math.Abs((int)block[k]) >> low;
                if (magnitude != 0)
                {
                    if (magnitude >> 1 != 0)
                    {
                        // Already nonzero: the correction bit
                        encoder.Encode(ref statistics[bin + 2], magnitude & 1);
                    }
                    else
                    {
                        encoder.Encode(ref statistics[bin + 1], 1);
                        encoder.EncodeFixed(block[k] < 0 ? 1 : 0);
                    }

                    break;
                }

                encoder.Encode(ref statistics[bin + 1], 0);
                bin += 3;
                k++;
            }
        }

        if (k <= end)
        {
            encoder.Encode(ref statistics[3 * (k - 1)], 1);
        }
    }

    /// <summary>Codes a DC difference and updates the class it gives to the next difference of the component (T.81 F.1.4.1, F.1.4.4.1).</summary>
    private void EncodeDcDifference(int component, int difference)
    {
        var statistics = _dcStatistics[GetTableIndex(component)];
        var context = _dcContexts[component];
        var encoder = Encoder;
        if (difference == 0)
        {
            encoder.Encode(ref statistics[context], 0);
            _dcContexts[component] = 0;
            return;
        }

        encoder.Encode(ref statistics[context], 1);
        var negative = difference < 0;
        var magnitude = Math.Abs(difference);
        encoder.Encode(ref statistics[context + 1], negative ? 1 : 0);
        EncodeMagnitude(statistics, context + 2 + (negative ? 1 : 0), DcCategoryBins, magnitude - 1);

        // The class of this difference (L = 0: no zero class for a nonzero difference): small up to 2^U, else large
        _dcContexts[component] = (magnitude <= 1 << DcUpper ? 4 : 12) + (negative ? 4 : 0);
    }

    /// <summary>
    /// Codes <c>Sz</c>, the magnitude minus one (T.81 figures F.8 and F.9): "is it zero" in <paramref name="firstBin"/>,
    /// then its category as decisions "is it at least <c>M</c>" for <c>M</c> doubling from 2 (from <paramref name="categoryBins"/>),
    /// then the bits below its leading one (14 bins further).
    /// </summary>
    private void EncodeMagnitude(byte[] statistics, int firstBin, int categoryBins, int sz)
    {
        var encoder = Encoder;
        if (sz == 0)
        {
            encoder.Encode(ref statistics[firstBin], 0);
            return;
        }

        encoder.Encode(ref statistics[firstBin], 1);
        var bin = categoryBins;
        var bound = 1;
        while (sz >> 1 >= bound)
        {
            encoder.Encode(ref statistics[bin], 1);
            bound <<= 1;
            bin++;
        }

        encoder.Encode(ref statistics[bin], 0);
        bin += BitBinsOffset;
        for (var bit = bound >> 1; bit != 0; bit >>= 1)
        {
            encoder.Encode(ref statistics[bin], (sz & bit) != 0 ? 1 : 0);
        }
    }

    /// <summary>
    /// Codes <c>Sz</c> of an AC coefficient (T.81 figures F.8 and F.9): the bin of the coefficient tells whether it is zero,
    /// then whether it is below 2; the next category decisions and the bits use the bins of the low or high frequencies.
    /// </summary>
    private void EncodeAcMagnitude(byte[] statistics, int coefficientBin, int sz, int k)
    {
        var encoder = Encoder;
        if (sz == 0)
        {
            encoder.Encode(ref statistics[coefficientBin], 0);
            return;
        }

        encoder.Encode(ref statistics[coefficientBin], 1);
        var bound = 1;
        var bin = coefficientBin;
        if (sz >> 1 != 0)
        {
            encoder.Encode(ref statistics[coefficientBin], 1);
            bound = 2;
            bin = k <= AcThreshold ? AcLowCategoryBins : AcHighCategoryBins;
            while (sz >> 1 >= bound)
            {
                encoder.Encode(ref statistics[bin], 1);
                bound <<= 1;
                bin++;
            }
        }

        encoder.Encode(ref statistics[bin], 0);
        bin += BitBinsOffset;
        for (var bit = bound >> 1; bit != 0; bit >>= 1)
        {
            encoder.Encode(ref statistics[bin], (sz & bit) != 0 ? 1 : 0);
        }
    }

    private void ResetStatistics()
    {
        foreach (var statistics in _dcStatistics)
        {
            statistics.AsSpan().Clear();
        }

        foreach (var statistics in _acStatistics)
        {
            statistics.AsSpan().Clear();
        }

        _predictions.AsSpan().Clear();
        _dcContexts.AsSpan().Clear();
    }
}
