namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one sequential arithmetic-coded scan (ITU-T T.81 section F.2.4, frames SOF9): the DC difference,
/// the AC coefficients, dequantization and the inverse DCT; the samples are written into the component planes.
/// </summary>
/// <remarks>The coded values are validated by <see cref="JpegArithmeticScanDecoder"/>; DC values outside 16 bits are <see cref="InvalidImageContentException"/>.</remarks>
internal sealed class JpegArithmeticSequentialScanDecoder : JpegArithmeticScanDecoder
{
    private readonly double[] _block = new double[64];

    /// <param name="components">The scan components with their conditioning, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="mcuRowDecoded">Called after each completed MCU row (block row for a non-interleaved scan) with its index.</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    public JpegArithmeticSequentialScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, Action<int>? mcuRowDecoded, CancellationToken cancellationToken)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded, cancellationToken)
    {
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        var dc = scanComponent.Predictor + DecodeDcDifference(scanComponent);
        if (dc is < short.MinValue or > short.MaxValue)
            throw Invalid("The JPEG DC coefficient is out of range.");

        scanComponent.Predictor = dc;
        var conditioning = scanComponent.Arithmetic!;
        var quantization = scanComponent.Quantization;
        var block = _block;
        var zigZag = JpegIdct.ZigZag;
        var hasAc = false;
        for (var k = 1; k < 64; k++)
        {
            if (!TryDecodeAcCoefficient(conditioning.AcStatistics, ref k, 63, conditioning.AcThreshold, out var value))
                break;

            var natural = zigZag[k];
            block[natural] = value * (double)quantization[natural];
            hasAc = true;
        }

        TransformBlock(scanComponent, blockRow, blockColumn, block, dc, hasAc);
    }
}
