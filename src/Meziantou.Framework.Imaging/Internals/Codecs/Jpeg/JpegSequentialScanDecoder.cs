namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one sequential Huffman scan (ITU-T T.81 section F.2): DC prediction, AC run-lengths,
/// dequantization and the inverse DCT; the samples are written into the component planes.
/// </summary>
/// <remarks>
/// Validation (all <see cref="InvalidImageContentException"/>), besides the scan structure of <see cref="JpegScanDecoder"/>:
/// invalid Huffman codes, DC categories above 11 and AC magnitudes above 10 (15 and 14 for 12-bit samples), coefficient
/// runs past the 63rd AC coefficient and DC values outside 16 bits.
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

    /// <summary>
    /// Gets a value indicating whether the AC coefficients whose code and magnitude fit in the lookup bits are decoded by one
    /// lookup (<see cref="JpegHuffmanTable.FastAc"/>), which needs a table per AC table. The coefficients are the same.
    /// </summary>
    public bool UsesFastAc
    {
        get;
        init
        {
            field = value;
            if (value)
            {
                foreach (var component in Components)
                {
                    component.AcTable!.PrepareFastAc();
                }
            }
        }
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        var reader = Reader;
        var quantization = scanComponent.Quantization;

        // DC: difference from the prediction (T.81 F.2.2.1)
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        if (category > MaxDcCategory)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG DC coefficient category is greater than {MaxDcCategory}."));

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
            // Most coefficients: the code and the magnitude in one lookup
            var fast = UsesFastAc ? reader.DecodeFastAc(acTable) : 0;
            if (fast != 0)
            {
                k += (fast >> 4) & 0x0F;
                if (k > 63)
                    throw Invalid("The JPEG AC coefficients run past the end of the block.");

                var position = zigZag[k];
                block[position] = (fast >> 8) * (double)quantization[position];
                hasAc = true;
                k++;
                continue;
            }

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

            if (size > MaxAcCategory)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG AC coefficient magnitude category is greater than {MaxAcCategory}."));

            var natural = zigZag[k];
            block[natural] = reader.ReceiveExtend(size) * (double)quantization[natural];
            hasAc = true;
            k++;
        }

        TransformBlock(scanComponent, blockRow, blockColumn, block, dc, hasAc);
    }
}
