namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the samples of one scan of a lossless frame (ITU-T T.81 Annex H, Huffman or arithmetic coding): the difference
/// of every sample, then the samples of each line from their prediction; the samples are written into the component planes.
/// </summary>
/// <remarks>
/// <para>
/// The data unit is one sample (T.81 H.1.1), so an MCU row is one line of a non-interleaved scan, or <c>V</c> lines of
/// each component of an interleaved one, with the samples that complete its MCUs (T.81 A.2.4), which are decoded like
/// the others and take part in the predictions.
/// </para>
/// <para>
/// A difference is a category <c>SSSS</c> coded with the DC table of the component, then <c>SSSS</c> bits (none for the
/// category 16, which is the difference 32,768; T.81 table H.2). The differences of an MCU row are kept, and its lines
/// are reconstructed when it is complete (T.81 H.1.2.1), with <c>Ra</c> the sample on the left, <c>Rb</c> the one above
/// and <c>Rc</c> the one above on the left:
/// </para>
/// <list type="bullet">
/// <item><description>
/// the first line of the scan and of each restart interval: <c>2^(P - Pt - 1)</c> for its first sample, then <c>Ra</c>;
/// </description></item>
/// <item><description>the first sample of any other line: <c>Rb</c>;</description></item>
/// <item><description>
/// any other sample, the predictor the scan selects: <c>Ra</c> (1), <c>Rb</c> (2), <c>Rc</c> (3), <c>Ra + Rb - Rc</c> (4),
/// <c>Ra + ((Rb - Rc) &gt;&gt; 1)</c> (5), <c>Rb + ((Ra - Rc) &gt;&gt; 1)</c> (6) or <c>(Ra + Rb) &gt;&gt; 1</c> (7), computed
/// without clamping, the shifts being arithmetic.
/// </description></item>
/// </list>
/// <para>
/// Arithmetic coding (T.81 H.1.4.3, SOF11): a difference is coded like a DC difference (<see cref="JpegArithmeticScanDecoder"/>)
/// but conditioned on two differences of the component: <c>Da</c>, the one on the left (zero at the start of a line), and
/// <c>Db</c>, the one above (zero on the first line of the scan and of each restart interval). Each is classified as zero,
/// small or large and positive or negative with the bounds <c>L</c> and <c>U</c> of the DC conditioning table, and the 25
/// pairs of classes select the four bins of the zero, sign and "magnitude is one" decisions; the magnitude categories and
/// bits have a set of bins for a large <c>Db</c> (<c>|Db| &gt; 2^U</c>) and one for the other values (table H.3). The
/// components that select the same conditioning table share its statistics.
/// </para>
/// <para>
/// A sample is its prediction plus its difference modulo 2^16. The samples are predicted from one another as they are
/// coded, that is divided by <c>2^Pt</c> (the point transform, T.81 A.4), and written multiplied by it.
/// </para>
/// <para>
/// Validation (all <see cref="InvalidImageContentException"/>), besides the scan structure of <see cref="JpegScanDecoder"/>:
/// invalid Huffman codes, categories above 16 (magnitudes above 2^15 with arithmetic coding), a restart interval that is not a whole number of MCU rows (T.81 H.1.1) and
/// samples that do not fit in the <c>P - Pt</c> bits the frame and the scan give them.
/// </para>
/// </remarks>
internal sealed class JpegLosslessScanDecoder : JpegScanDecoder
{
    private readonly int _predictor;
    private readonly int _pointTransform;
    private readonly int _sampleBits;
    private readonly JpegArithmeticDecoder? _arithmetic;
    private bool _isFirstLine = true;

    /// <param name="components">The scan components with their DC table, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="predictor">The predictor the scan selects, 1 to 7.</param>
    /// <param name="pointTransform">The point transform of the scan, below the precision of the frame.</param>
    /// <param name="mcuRowDecoded">Called after each completed MCU row (line for a non-interleaved scan) with its index.</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    /// <param name="arithmetic">
    /// Whether the scan is arithmetic coded: the components then have their conditioning
    /// (<see cref="JpegScanDecoder.ScanComponent.Arithmetic"/>, with <see cref="JpegArithmeticConditioning.LosslessBinCount"/> DC bins).
    /// </param>
    public JpegLosslessScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, int predictor, int pointTransform, Action<int>? mcuRowDecoded, CancellationToken cancellationToken, bool arithmetic = false)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded, cancellationToken, arithmetic, lossless: true)
    {
        if (restartInterval % McusPerLine != 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG restart interval of a lossless scan ({restartInterval} MCUs) is not a whole number of MCU rows ({McusPerLine} MCUs)."));

        _predictor = predictor;
        _pointTransform = pointTransform;
        _sampleBits = components[0].Component.Precision - pointTransform;
        if (arithmetic)
        {
            _arithmetic = new JpegArithmeticDecoder(reader);
            foreach (var scanComponent in components)
            {
                var component = scanComponent.Component;
                scanComponent.LeftDifferences = new int[IsInterleaved ? component.VerticalSampling : 1];
                scanComponent.AboveDifferences = new int[component.Stride];
            }

            ResetArithmeticState();
        }
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        // The line of the MCU row: the first one of the buffer is the last line of the previous MCU row
        var component = scanComponent.Component;
        var lines = IsInterleaved ? component.VerticalSampling : 1;
        var line = lines == 1 ? 1 : 1 + (blockRow % lines);
        if (_arithmetic is not null)
        {
            component.Differences[(line * component.Stride) + blockColumn] = (ushort)DecodeArithmeticDifference(scanComponent, line - 1, blockColumn);
            return;
        }

        // The difference of one sample (T.81 H.1.2.2)
        var reader = Reader;
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        int difference;
        if (category == 0)
        {
            difference = 0;
        }
        else if (category < 16)
        {
            difference = reader.ReceiveExtend(category);
        }
        else if (category == 16)
        {
            difference = 32768;
        }
        else
        {
            throw Invalid("The JPEG lossless difference category is greater than 16.");
        }

        component.Differences[(line * component.Stride) + blockColumn] = (ushort)difference;
    }

    protected override void OnMcuRowDecoded(int row)
    {
        foreach (var scanComponent in Components)
        {
            var component = scanComponent.Component;
            var lines = IsInterleaved ? component.VerticalSampling : 1;
            var width = IsInterleaved ? component.Stride : component.BlocksPerLine;
            var stride = component.Stride;
            var buffer = component.Differences;
            for (var line = 0; line < lines; line++)
            {
                var samples = buffer.Slice((line + 1) * stride, width);
                if (_isFirstLine && line == 0)
                {
                    ReconstructFirstLine(samples, 1 << (_sampleBits - 1));
                }
                else
                {
                    Reconstruct(samples, buffer.Slice(line * stride, width), _predictor);
                }

                Store(component, (row * lines) + line, samples);
            }

            // The last line predicts the first one of the next MCU row
            buffer.Slice(lines * stride, width).CopyTo(buffer);
        }

        _isFirstLine = false;
        foreach (var scanComponent in Components)
        {
            // A line starts without a difference on its left
            scanComponent.LeftDifferences.AsSpan().Clear();
        }
    }

    protected override void OnRestart()
    {
        // The prediction is reset: the next line is predicted like the first one of the scan (T.81 H.1.1, H.1.2.1); the
        // arithmetic coding restarts with new statistics and no differences around the next sample
        base.OnRestart();
        _isFirstLine = true;
        if (_arithmetic is not null)
        {
            ResetArithmeticState();
        }
    }

    private void ResetArithmeticState()
    {
        _arithmetic!.Reset();
        foreach (var scanComponent in Components)
        {
            scanComponent.Arithmetic!.DcStatistics.AsSpan().Clear();
            scanComponent.LeftDifferences.AsSpan().Clear();
            scanComponent.AboveDifferences.AsSpan().Clear();
        }
    }

    /// <summary>Decodes the difference of one sample of an arithmetic-coded scan (T.81 H.1.4.3) and records it as a neighbor of the next ones.</summary>
    /// <param name="scanComponent">The component of the sample.</param>
    /// <param name="line">The line of the sample in the MCU row of the component.</param>
    /// <param name="column">The column of the sample.</param>
    private int DecodeArithmeticDifference(ScanComponent scanComponent, int line, int column)
    {
        var decoder = _arithmetic!;
        var conditioning = scanComponent.Arithmetic!;
        var statistics = conditioning.DcStatistics;
        var lower = conditioning.DcLower;
        var upper = conditioning.DcUpper;
        ref var left = ref scanComponent.LeftDifferences[line];
        ref var above = ref scanComponent.AboveDifferences[column];
        var context = 4 * ((5 * Classify(left, lower, upper)) + Classify(above, lower, upper));
        var difference = 0;
        if (decoder.Decode(ref statistics[context]) != 0)
        {
            var negative = decoder.Decode(ref statistics[context + 1]) != 0;
            var magnitude = 1;
            if (decoder.Decode(ref statistics[context + 2 + (negative ? 1 : 0)]) != 0)
            {
                // The categories and the bits of the magnitude, with the bins of a large or of a small difference above
                var bin = Math.Abs(above) > 1 << upper ? JpegArithmeticConditioning.LosslessLargeMagnitudeBins : JpegArithmeticConditioning.LosslessSmallMagnitudeBins;
                var last = bin + 14;
                var bound = 2;
                while (decoder.Decode(ref statistics[bin]) != 0)
                {
                    if (bin == last)
                        throw Invalid("The JPEG lossless difference is out of range.");

                    bound <<= 1;
                    bin++;
                }

                var size = bound >> 1;
                bin += 14;
                for (var bit = size >> 1; bit != 0; bit >>= 1)
                {
                    if (decoder.Decode(ref statistics[bin]) != 0)
                    {
                        size |= bit;
                    }
                }

                magnitude = size + 1;
            }

            difference = negative ? -magnitude : magnitude;
        }

        left = difference;
        above = difference;
        return difference;
    }

    /// <summary>
    /// Gets the class of a neighboring difference (T.81 F.1.4.4.1.2, H.1.4.3.1): 0 for zero (at most <c>2^L / 2</c> in
    /// magnitude), 1 and 2 for small positive and negative differences (at most <c>2^U</c>), 3 and 4 for large ones.
    /// </summary>
    private static int Classify(int difference, int lower, int upper)
    {
        var magnitude = Math.Abs(difference);
        if (magnitude <= (1 << lower) >> 1)
            return 0;

        var large = magnitude > 1 << upper ? 2 : 0;
        return 1 + large + (difference < 0 ? 1 : 0);
    }

    /// <summary>Replaces the differences of the first line of a scan or of a restart interval by its samples.</summary>
    /// <param name="samples">The differences of the line.</param>
    /// <param name="initial">The prediction of the first sample.</param>
    internal static void ReconstructFirstLine(Span<ushort> samples, int initial)
    {
        var previous = initial;
        for (var x = 0; x < samples.Length; x++)
        {
            previous = (ushort)(previous + samples[x]);
            samples[x] = (ushort)previous;
        }
    }

    /// <summary>Replaces the differences of a line by its samples.</summary>
    /// <param name="samples">The differences of the line.</param>
    /// <param name="above">The samples of the line above.</param>
    /// <param name="predictor">The predictor the scan selects, 1 to 7.</param>
    internal static void Reconstruct(Span<ushort> samples, ReadOnlySpan<ushort> above, int predictor)
    {
        above = above[..samples.Length];
        samples[0] = (ushort)(above[0] + samples[0]);
        switch (predictor)
        {
            case 1:
                for (var x = 1; x < samples.Length; x++)
                {
                    samples[x] = (ushort)(samples[x - 1] + samples[x]);
                }

                break;

            case 2:
                for (var x = 1; x < samples.Length; x++)
                {
                    samples[x] = (ushort)(above[x] + samples[x]);
                }

                break;

            case 3:
                for (var x = 1; x < samples.Length; x++)
                {
                    samples[x] = (ushort)(above[x - 1] + samples[x]);
                }

                break;

            case 4:
                for (var x = 1; x < samples.Length; x++)
                {
                    samples[x] = (ushort)(samples[x - 1] + above[x] - above[x - 1] + samples[x]);
                }

                break;

            case 5:
                for (var x = 1; x < samples.Length; x++)
                {
                    samples[x] = (ushort)(samples[x - 1] + ((above[x] - above[x - 1]) >> 1) + samples[x]);
                }

                break;

            case 6:
                for (var x = 1; x < samples.Length; x++)
                {
                    samples[x] = (ushort)(above[x] + ((samples[x - 1] - above[x - 1]) >> 1) + samples[x]);
                }

                break;

            case 7:
                for (var x = 1; x < samples.Length; x++)
                {
                    samples[x] = (ushort)(((samples[x - 1] + above[x]) >> 1) + samples[x]);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(predictor), predictor, "The predictor of a lossless scan is between 1 and 7.");
        }
    }

    /// <summary>Writes a line of samples into the plane of its component, multiplied by the point transform.</summary>
    private void Store(JpegFrameComponent component, int row, ReadOnlySpan<ushort> samples)
    {
        var offset = component.GetRowOffset(row);
        var shift = _pointTransform;
        var all = 0;
        if (component.HasDeepSamples)
        {
            var destination = component.DeepSamples.Slice(offset, samples.Length);
            for (var x = 0; x < samples.Length; x++)
            {
                all |= samples[x];
                destination[x] = (ushort)(samples[x] << shift);
            }
        }
        else
        {
            var destination = component.Samples.AsSpan(offset, samples.Length);
            for (var x = 0; x < samples.Length; x++)
            {
                all |= samples[x];
                destination[x] = (byte)(samples[x] << shift);
            }
        }

        if (all >> _sampleBits != 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"A JPEG lossless sample of the component {component.Id} does not fit in {_sampleBits} bits."));
    }
}
