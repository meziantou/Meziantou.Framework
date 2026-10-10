namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one sequential Huffman scan (ITU-T T.81 section F.2): DC prediction, AC run-lengths,
/// dequantization and the inverse DCT; the samples are written into the component planes.
/// </summary>
/// <remarks>
/// Validation (all <see cref="InvalidImageContentException"/>), besides the scan structure of <see cref="JpegScanDecoder"/>:
/// invalid Huffman codes, DC categories above 11 and AC magnitudes above 10 (8-bit precision), coefficient runs past the
/// 63rd AC coefficient and DC values outside 16 bits.
/// </remarks>
internal sealed class JpegSequentialScanDecoder : JpegScanDecoder
{
    private readonly double[] _block = new double[64];

    /// <param name="components">The scan components with their tables, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="mcuRowDecoded">Called after each completed MCU row (block row for a non-interleaved scan) with its index.</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    public JpegSequentialScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, Action<int>? mcuRowDecoded, CancellationToken cancellationToken)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded, cancellationToken)
    {
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        var reader = Reader;
        var quantization = scanComponent.Quantization;

        // DC: difference from the prediction (T.81 F.2.2.1)
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        if (category > 11)
            throw Invalid("The JPEG DC coefficient category is greater than 11.");

        var dc = scanComponent.Predictor + (category == 0 ? 0 : reader.ReceiveExtend(category));
        if (dc is < short.MinValue or > short.MaxValue)
            throw Invalid("The JPEG DC coefficient is out of range.");

        scanComponent.Predictor = dc;

        // AC: run-length/size symbols in zig-zag order (T.81 F.2.2.2)
        var block = _block;
        var zigZag = JpegIdct.ZigZag;
        var acTable = scanComponent.AcTable!;
        var hasAc = false;
        for (var k = 1; k < 64;)
        {
            var symbol = reader.DecodeHuffman(acTable);
            var run = symbol >> 4;
            var size = symbol & 0x0F;
            if (size == 0)
            {
                if (run != 15)
                    break; // EOB

                k += 16; // ZRL
                continue;
            }

            k += run;
            if (k > 63)
                throw Invalid("The JPEG AC coefficients run past the end of the block.");

            if (size > 10)
                throw Invalid("The JPEG AC coefficient magnitude category is greater than 10.");

            var natural = zigZag[k];
            block[natural] = reader.ReceiveExtend(size) * (double)quantization[natural];
            hasAc = true;
            k++;
        }

        var component = scanComponent.Component;
        var samples = component.Samples.AsSpan(component.GetRowOffset(blockRow * 8) + (blockColumn * 8));
        if (hasAc)
        {
            block[0] = dc * (double)quantization[0];
            JpegIdct.Transform(block, samples, component.Stride);
            Array.Clear(block);
        }
        else
        {
            JpegIdct.TransformDcOnly((long)dc * quantization[0], samples, component.Stride);
        }
    }
}
