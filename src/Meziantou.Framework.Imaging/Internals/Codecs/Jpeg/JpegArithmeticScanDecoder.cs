namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoding procedures shared by the arithmetic-coded scans (ITU-T T.81 section F.2.4): the DC difference and the AC
/// coefficients as sequences of binary decisions, each with the statistics bin its context selects.
/// </summary>
/// <remarks>
/// <para>
/// A value <c>V</c> is coded as: zero or not; its sign; then its magnitude minus one, <c>Sz</c>, as a magnitude category
/// (a unary code: is <c>Sz</c> zero, below 2, below 4...) followed by the bits of <c>Sz</c> below its leading one.
/// </para>
/// <para>
/// DC difference (table F.4): the first four bins depend on the class of the previous difference of the component (zero,
/// small or large and its sign, with the bounds <c>L</c> and <c>U</c> of the conditioning table): zero, sign, and "is
/// <c>Sz</c> zero" for each sign; then 15 category bins and 14 bit bins.
/// </para>
/// <para>
/// AC coefficients (table F.5): for the coefficient <c>k</c>, the bins <c>3(k - 1)</c> (end of block, only decided after a
/// nonzero coefficient or at the start of the band), <c>+1</c> (zero or not) and <c>+2</c> (is <c>Sz</c> zero, and is it
/// below 2); the sign has the fixed estimate of one half; the 14 other category bins and the 14 bit bins are those of the
/// low frequencies up to <c>Kx</c>, or those of the high frequencies.
/// </para>
/// <para>
/// Validation (all <see cref="InvalidImageContentException"/>), besides the scan structure of
/// <see cref="JpegScanDecoder"/>: magnitudes beyond those of the sample precision (as for Huffman coding: 11 bits for a DC
/// difference and 10 for an AC coefficient with 8-bit samples, 15 and 14 with 12-bit samples) and runs of zeros past the
/// end of the block or of the band. Entropy-coded data that is too short reads as zeros (see
/// <see cref="JpegArithmeticDecoder"/>): it decodes to something, and is not reported.
/// </para>
/// </remarks>
internal abstract class JpegArithmeticScanDecoder : JpegScanDecoder
{
    // Table F.4: the first magnitude category bin of a DC table; the bin of the bits of a category is 14 bins further
    private const int DcCategoryBins = 20;

    // Table F.5: the category bins of the coefficients up to Kx, and of the others
    private const int AcLowCategoryBins = 189;
    private const int AcHighCategoryBins = 217;
    private const int BitBinsOffset = 14;

    private readonly JpegArithmeticDecoder _decoder;

    protected JpegArithmeticScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, Action<int>? mcuRowDecoded, CancellationToken cancellationToken)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded, cancellationToken, arithmetic: true)
    {
        _decoder = new JpegArithmeticDecoder(reader);
        ResetStatistics();
    }

    /// <summary>Gets the decoder of the binary decisions.</summary>
    protected JpegArithmeticDecoder Decoder => _decoder;

    /// <summary>A restart marker also resets the statistics, the DC classes and the decoder (T.81 F.2.4.4).</summary>
    protected override void OnRestart()
    {
        base.OnRestart();
        ResetStatistics();
    }

    /// <summary>Decodes a DC difference and updates the class it gives to the next difference of the component (T.81 F.2.4.1).</summary>
    protected int DecodeDcDifference(ScanComponent scanComponent)
    {
        var conditioning = scanComponent.Arithmetic!;
        var statistics = conditioning.DcStatistics;
        var context = conditioning.DcContext;
        if (_decoder.Decode(ref statistics[context]) == 0)
        {
            conditioning.DcContext = 0;
            return 0;
        }

        var negative = _decoder.Decode(ref statistics[context + 1]) != 0;
        var magnitude = 1;
        if (_decoder.Decode(ref statistics[context + 2 + (negative ? 1 : 0)]) != 0)
        {
            magnitude = DecodeMagnitude(statistics, DcCategoryBins, 15, MaxDcCategory, "The JPEG DC difference is out of range.");
        }

        // The class of this difference: zero up to 2^(L - 1) (only zero itself for L = 0), small up to 2^U, else large
        var lower = conditioning.DcLower == 0 ? 0 : 1 << (conditioning.DcLower - 1);
        conditioning.DcContext = magnitude <= lower ? 0 : (magnitude <= 1 << conditioning.DcUpper ? 4 : 12) + (negative ? 4 : 0);
        return negative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Decodes the next nonzero AC coefficient of a band (T.81 F.2.4.2): the end-of-block decision, the zero decisions of
    /// the coefficients it skips, its sign and its magnitude.
    /// </summary>
    /// <param name="statistics">The statistics area of the AC table.</param>
    /// <param name="k">The coefficient the decoding starts at (zig-zag index); receives the index of the decoded coefficient.</param>
    /// <param name="end">The last coefficient of the band.</param>
    /// <param name="threshold">Kx.</param>
    /// <param name="value">The coefficient.</param>
    /// <returns><see langword="false"/> at the end of the block: no coefficient from <paramref name="k"/> to <paramref name="end"/>.</returns>
    protected bool TryDecodeAcCoefficient(byte[] statistics, ref int k, int end, int threshold, out int value)
    {
        value = 0;
        var bin = 3 * (k - 1);
        if (_decoder.Decode(ref statistics[bin]) != 0)
            return false;

        while (_decoder.Decode(ref statistics[bin + 1]) == 0)
        {
            bin += 3;
            k++;
            if (k > end)
                throw Invalid(end == 63 ? "The JPEG AC coefficients run past the end of the block." : "The JPEG AC coefficients run past the end of the spectral band.");
        }

        var negative = _decoder.DecodeFixed() != 0;
        var magnitude = 1;

        // The same bin tells whether the magnitude is above 1, then whether it is above 2
        if (_decoder.Decode(ref statistics[bin + 2]) != 0)
        {
            magnitude = 2;
            if (_decoder.Decode(ref statistics[bin + 2]) != 0)
            {
                magnitude = DecodeMagnitude(statistics, k <= threshold ? AcLowCategoryBins : AcHighCategoryBins, 14, MaxAcCategory, "The JPEG AC coefficient is out of range.", first: 4);
            }
        }

        value = negative ? -magnitude : magnitude;
        return true;
    }

    /// <summary>
    /// Decodes a magnitude from its category (T.81 figure F.23: the decisions "is <c>Sz</c> at least <c>M</c>" for
    /// <c>M</c> doubling from <paramref name="first"/>, one bin each) and the bits of <c>Sz</c> below its leading one (figure
    /// F.24, in the bin 14 bins after the one of the category).
    /// </summary>
    /// <param name="statistics">The statistics area.</param>
    /// <param name="bin">The bin of the first category decision.</param>
    /// <param name="categoryBins">The number of category bins.</param>
    /// <param name="maxBits">The largest number of bits of a magnitude with the precision of the samples.</param>
    /// <param name="error">The message of the exception for a magnitude out of range.</param>
    /// <param name="first">The first bound: <c>Sz</c> is known to be at least half of it.</param>
    /// <returns>The magnitude: <c>Sz + 1</c>.</returns>
    private int DecodeMagnitude(byte[] statistics, int bin, int categoryBins, int maxBits, string error, int first = 2)
    {
        // Sz is at least bound / 2 and, once the category is known, below bound
        var bound = first;
        var last = bin + categoryBins - 1;
        while (_decoder.Decode(ref statistics[bin]) != 0)
        {
            if (bin == last)
                throw Invalid(error);

            bound <<= 1;
            bin++;
        }

        var size = bound >> 1;
        bin += BitBinsOffset;
        for (var bit = size >> 1; bit != 0; bit >>= 1)
        {
            if (_decoder.Decode(ref statistics[bin]) != 0)
            {
                size |= bit;
            }
        }

        var magnitude = size + 1;
        if (magnitude >= 1 << maxBits)
            throw Invalid(error);

        return magnitude;
    }

    private void ResetStatistics()
    {
        _decoder.Reset();
        foreach (var scanComponent in Components)
        {
            var conditioning = scanComponent.Arithmetic!;
            conditioning.DcStatistics.AsSpan().Clear();
            conditioning.AcStatistics.AsSpan().Clear();
            conditioning.DcContext = 0;
        }
    }
}
