namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one progressive Huffman scan (ITU-T T.81 section G.1.2) into the coefficients of the components
/// (<see cref="JpegFrameComponent.GetCoefficients"/>): DC first scans (prediction, values shifted left by <c>Al</c>), DC
/// refinements (one bit per block), AC first scans of a spectral band (run-lengths, EOB runs spanning blocks) and AC
/// refinements (sign bits of newly nonzero coefficients and correction bits of the others, ZRL and EOB runs).
/// </summary>
/// <remarks>
/// <para>
/// The walker validated the spectral selection, the successive approximation and the progression of every coefficient
/// (<see cref="JpegStructureParser"/>). Validation here (all <see cref="InvalidImageContentException"/>), besides the scan
/// structure of <see cref="JpegScanDecoder"/>: invalid Huffman codes, DC categories above 11, AC magnitudes above 10 (8-bit
/// precision), coefficients whose value shifted by <c>Al</c> does not fit in 16 bits (DC: two's complement range; AC:
/// magnitude at most 32,767, so that refinements cannot overflow), runs past the end of the band, and refinement
/// magnitudes other than 1.
/// </para>
/// <para>
/// An EOB run is reset at every restart marker; the part of a run that extends past the end of the scan or of a restart
/// interval is ignored, like the other bits after the last MCU. A ZRL that ends past the band is tolerated like in
/// sequential scans.
/// </para>
/// </remarks>
internal sealed class JpegProgressiveScanDecoder : JpegScanDecoder
{
    private readonly int _start;
    private readonly int _end;
    private readonly int _low;
    private readonly bool _refinement;
    private int _endOfBandRun;

    /// <param name="components">The scan components with their tables, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved (DC) scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved (DC) scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="start">The first coefficient of the band (<c>Ss</c>, zig-zag order).</param>
    /// <param name="end">The last coefficient of the band (<c>Se</c>).</param>
    /// <param name="high">The bit position of the previous scan of the band (<c>Ah</c>), 0 for a first scan.</param>
    /// <param name="low">The bit position of this scan (<c>Al</c>).</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    public JpegProgressiveScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, int start, int end, int high, int low, CancellationToken cancellationToken)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded: null, cancellationToken)
    {
        _start = start;
        _end = end;
        _low = low;
        _refinement = high != 0;
    }

    protected override void OnRestart()
    {
        base.OnRestart();
        _endOfBandRun = 0;
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        var block = scanComponent.Component.GetCoefficients(blockRow, blockColumn);
        if (_start == 0)
        {
            if (_refinement)
            {
                // T.81 G.1.2.1: the next bit of the DC value (two's complement, as the first scan shifted it arithmetically)
                if (Reader.ReadBits(1) != 0)
                {
                    block[0] |= (short)(1 << _low);
                }
            }
            else
            {
                DecodeDcFirst(scanComponent, block);
            }
        }
        else if (_refinement)
        {
            DecodeAcRefinement(scanComponent.AcTable!, block);
        }
        else
        {
            DecodeAcFirst(scanComponent.AcTable!, block);
        }
    }

    /// <summary>T.81 G.1.2.1: the DC difference from the prediction, in the point-transformed domain.</summary>
    private void DecodeDcFirst(ScanComponent scanComponent, Span<short> block)
    {
        var reader = Reader;
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        if (category > 11)
            throw Invalid("The JPEG DC coefficient category is greater than 11.");

        var dc = scanComponent.Predictor + (category == 0 ? 0 : reader.ReceiveExtend(category));
        var value = (long)dc << _low;
        if (value is < short.MinValue or > short.MaxValue)
            throw Invalid("The JPEG DC coefficient is out of range.");

        scanComponent.Predictor = dc;
        block[0] = (short)value;
    }

    /// <summary>T.81 G.1.2.2: run-length coding of the band, with end-of-band runs that span blocks.</summary>
    private void DecodeAcFirst(JpegHuffmanTable table, Span<short> block)
    {
        if (_endOfBandRun > 0)
        {
            _endOfBandRun--;
            return;
        }

        var reader = Reader;
        var zigZag = JpegIdct.ZigZag;
        for (var k = _start; k <= _end;)
        {
            var symbol = reader.DecodeHuffman(table);
            var run = symbol >> 4;
            var size = symbol & 0x0F;
            if (size == 0)
            {
                if (run != 15)
                {
                    // EOBn: this block and the next 2^n - 1 + (n extra bits) blocks end here
                    _endOfBandRun = (1 << run) - 1 + (run == 0 ? 0 : reader.ReadBits(run));
                    return;
                }

                k += 16; // ZRL
                continue;
            }

            k += run;
            if (k > _end)
                throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

            if (size > 10)
                throw Invalid("The JPEG AC coefficient magnitude category is greater than 10.");

            var value = (long)reader.ReceiveExtend(size) << _low;
            if (Math.Abs(value) > short.MaxValue)
                throw Invalid("The JPEG AC coefficient is out of range.");

            block[zigZag[k]] = (short)value;
            k++;
        }
    }

    /// <summary>
    /// T.81 G.1.2.3: in the band, each symbol places one newly nonzero coefficient (magnitude <c>2^Al</c>, its sign bit) after
    /// <c>run</c> coefficients that are still zero; a correction bit follows for every coefficient already nonzero that the
    /// symbol passes over (or, for an EOB run, for every remaining one in each block of the run).
    /// </summary>
    private void DecodeAcRefinement(JpegHuffmanTable table, Span<short> block)
    {
        var reader = Reader;
        var zigZag = JpegIdct.ZigZag;
        var positive = 1 << _low;
        var k = _start;
        if (_endOfBandRun == 0)
        {
            for (; k <= _end; k++)
            {
                var symbol = reader.DecodeHuffman(table);
                var run = symbol >> 4;
                var size = symbol & 0x0F;
                var value = 0;
                if (size != 0)
                {
                    if (size != 1)
                        throw Invalid("A JPEG AC refinement scan codes a magnitude other than 1.");

                    value = reader.ReadBits(1) != 0 ? positive : -positive;
                }
                else if (run != 15)
                {
                    // EOBn: the rest of this block and of the next blocks of the run only have correction bits
                    _endOfBandRun = (1 << run) + (run == 0 ? 0 : reader.ReadBits(run));
                    break;
                }

                // Skip 'run' coefficients that are still zero (16 for ZRL), refining the nonzero ones on the way
                for (; k <= _end; k++)
                {
                    ref var coefficient = ref block[zigZag[k]];
                    if (coefficient != 0)
                    {
                        Refine(ref coefficient, positive);
                    }
                    else
                    {
                        if (run == 0)
                            break;

                        run--;
                    }
                }

                if (value != 0)
                {
                    if (k > _end)
                        throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

                    block[zigZag[k]] = (short)value;
                }
            }
        }

        if (_endOfBandRun > 0)
        {
            for (; k <= _end; k++)
            {
                ref var coefficient = ref block[zigZag[k]];
                if (coefficient != 0)
                {
                    Refine(ref coefficient, positive);
                }
            }

            _endOfBandRun--;
        }
    }

    /// <summary>Applies the correction bit of a coefficient that is already nonzero: adds <c>2^Al</c> to its magnitude when set.</summary>
    private void Refine(ref short coefficient, int bit)
    {
        // The bits below the previous position are zero, so the magnitude stays at most 32,767 (checked by the first scan)
        if (Reader.ReadBits(1) != 0 && (Math.Abs((int)coefficient) & bit) == 0)
        {
            coefficient = (short)(coefficient >= 0 ? coefficient + bit : coefficient - bit);
        }
    }
}
