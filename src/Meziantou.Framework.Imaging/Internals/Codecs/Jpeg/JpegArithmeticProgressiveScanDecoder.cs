namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one progressive arithmetic-coded scan (ITU-T T.81 section G.1.3, frames SOF10) into the
/// coefficients of the components (<see cref="JpegFrameComponent.GetCoefficients"/>): DC first scans (the differences of the
/// values shifted right by <c>Al</c>), DC refinements (one decision per block, with the fixed estimate), AC first scans of
/// a spectral band, and AC refinements.
/// </summary>
/// <remarks>
/// <para>
/// The walker validated the spectral selection, the successive approximation and the progression of every coefficient
/// (<see cref="JpegStructureParser"/>). Validation here (all <see cref="InvalidImageContentException"/>), besides the
/// coded values (<see cref="JpegArithmeticScanDecoder"/>): coefficients whose value shifted by <c>Al</c> does not fit in
/// 16 bits (DC: two's complement range; AC: magnitude at most 32,767, so that refinements cannot overflow).
/// </para>
/// <para>
/// AC refinement (G.1.3.3): the bin of the end-of-block decision of a coefficient is only used past the last coefficient
/// that the previous scans made nonzero, as the block cannot end before it; a coefficient that is already nonzero has a
/// correction decision (the third bin of the coefficient), one that is still zero a decision that tells whether it becomes
/// nonzero (the second bin), followed by its sign with the fixed estimate.
/// </para>
/// </remarks>
internal sealed class JpegArithmeticProgressiveScanDecoder : JpegArithmeticScanDecoder
{
    private readonly int _start;
    private readonly int _end;
    private readonly int _low;
    private readonly bool _refinement;

    /// <param name="components">The scan components with their conditioning, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved (DC) scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved (DC) scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="start">The first coefficient of the band (<c>Ss</c>, zig-zag order).</param>
    /// <param name="end">The last coefficient of the band (<c>Se</c>).</param>
    /// <param name="high">The bit position of the previous scan of the band (<c>Ah</c>), 0 for a first scan.</param>
    /// <param name="low">The bit position of this scan (<c>Al</c>).</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    public JpegArithmeticProgressiveScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, int start, int end, int high, int low, CancellationToken cancellationToken)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded: null, cancellationToken)
    {
        _start = start;
        _end = end;
        _low = low;
        _refinement = high != 0;
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        var block = scanComponent.Component.GetCoefficients(blockRow, blockColumn);
        if (_start == 0)
        {
            if (_refinement)
            {
                // The next bit of the DC value (two's complement, as the first scan shifted it arithmetically)
                if (Decoder.DecodeFixed() != 0)
                {
                    block[0] |= (short)(1 << _low);
                }
            }
            else
            {
                var dc = scanComponent.Predictor + DecodeDcDifference(scanComponent);
                var value = (long)dc << _low;
                if (value is < short.MinValue or > short.MaxValue)
                    throw Invalid("The JPEG DC coefficient is out of range.");

                scanComponent.Predictor = dc;
                block[0] = (short)value;
            }
        }
        else if (_refinement)
        {
            DecodeAcRefinement(scanComponent.Arithmetic!.AcStatistics, block);
        }
        else
        {
            DecodeAcFirst(scanComponent.Arithmetic!, block);
        }
    }

    /// <summary>T.81 G.1.3.2: the coefficients of the band, like those of a sequential block, shifted left by <c>Al</c>.</summary>
    private void DecodeAcFirst(JpegArithmeticConditioning conditioning, Span<short> block)
    {
        var zigZag = JpegIdct.ZigZag;
        for (var k = _start; k <= _end; k++)
        {
            if (!TryDecodeAcCoefficient(conditioning.AcStatistics, ref k, _end, conditioning.AcThreshold, out var coefficient))
                break;

            var value = (long)coefficient << _low;
            if (Math.Abs(value) > short.MaxValue)
                throw Invalid("The JPEG AC coefficient is out of range.");

            block[zigZag[k]] = (short)value;
        }
    }

    /// <summary>T.81 G.1.3.3: one more bit of the coefficients that are already nonzero, and the coefficients that become nonzero.</summary>
    private void DecodeAcRefinement(byte[] statistics, Span<short> block)
    {
        var decoder = Decoder;
        var zigZag = JpegIdct.ZigZag;
        var bit = 1 << _low;

        // The last coefficient of the band that the previous scans made nonzero: the block does not end before it
        var lastNonzero = _end;
        while (lastNonzero >= _start && block[zigZag[lastNonzero]] == 0)
        {
            lastNonzero--;
        }

        for (var k = _start; k <= _end; k++)
        {
            var bin = 3 * (k - 1);
            if (k > lastNonzero && decoder.Decode(ref statistics[bin]) != 0)
                break;

            while (true)
            {
                ref var coefficient = ref block[zigZag[k]];
                if (coefficient != 0)
                {
                    // The bits below the previous position are zero, so the magnitude stays at most 32,767
                    if (decoder.Decode(ref statistics[bin + 2]) != 0)
                    {
                        coefficient = (short)(coefficient < 0 ? coefficient - bit : coefficient + bit);
                    }

                    break;
                }

                if (decoder.Decode(ref statistics[bin + 1]) != 0)
                {
                    coefficient = (short)(decoder.DecodeFixed() != 0 ? -bit : bit);
                    break;
                }

                bin += 3;
                k++;
                if (k > _end)
                    throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");
            }
        }
    }
}
