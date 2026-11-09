using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes one scan of a JPEG-LS frame (ITU-T T.87 Annex A, the LOCO-I algorithm) into the planes of its components:
/// the context modeling of the regular mode (gradients quantized by the thresholds, median edge detection, bias correction),
/// the run mode and its interruption samples, and the limited-length Golomb codes of the prediction errors.
/// </summary>
/// <remarks>
/// <para>
/// The entropy-coded data of the scan is buffered (charged to the scope as decoder state) and decoded once the scan ends.
/// After a byte <c>0xFF</c>, the most significant bit of the next byte is a stuffed zero (T.87 A.1): the byte carries 7 bits
/// of data, and a byte with that bit set is a marker, which ends the data.
/// </para>
/// <para>
/// A component is coded line by line, from the line above and the samples on the left (zero above the first line, the
/// first sample of the line above on the left of a line, the last sample of the line above on its right). The three
/// interleave modes (T.87 A.2.1, B.3): one component per scan (<c>ILV = 0</c>); the lines of the components in turn, <c>V</c>
/// lines of each component per line of MCUs (<c>ILV = 1</c>), each component with its own run index; the samples of
/// components of the same size in turn (<c>ILV = 2</c>), in run mode when every component is, with one run index. The
/// 365 regular contexts and the two run interruption contexts are shared by the components of the scan.
/// </para>
/// <para>
/// Restart intervals (T.87 D.2.1, as CharLS reads them) count lines: one line of the component (<c>ILV = 0</c>), one line
/// of MCUs (<c>ILV = 1</c>) or one line of samples (<c>ILV = 2</c>). An interval ends on a byte boundary, with a restart
/// marker, after which the contexts and the run indexes are reset and the line above is zero again, as for the first line.
/// </para>
/// <para>
/// Validation (all <see cref="InvalidImageContentException"/>): entropy-coded data that ends before the last sample, a
/// marker inside it, a missing or out-of-sequence restart marker, an escape code that is longer than the limit, a Golomb parameter or a prediction error out of the
/// range of the samples, and a run past the end of its line.
/// </para>
/// </remarks>
internal sealed class JpegLsScanDecoder : IDisposable
{
    private const int RegularContexts = 365;

    // T.87 A.7.1.1: the order of the run lengths, indexed by the run index
    private static ReadOnlySpan<byte> RunOrders => [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    private readonly JpegFrameComponent[] _components;
    private readonly JpegLsParameters _parameters;
    private readonly int _interleave;
    private readonly int _restartInterval;
    private int _expectedRestart;
    private readonly AllocationScope _scope;
    private readonly ImageResourceLimits _limits;
    private readonly CancellationToken _cancellationToken;

    // The contexts: 365 regular ones, then the two of the run interruption samples (RItype 0 and 1)
    private readonly long[] _a = new long[RegularContexts + 2];
    private readonly int[] _b = new int[RegularContexts + 2];
    private readonly int[] _c = new int[RegularContexts];
    private readonly int[] _n = new int[RegularContexts + 2];
    private readonly int[] _runIndex;

    // The line above and the current line of each component, with a sample before the first one and one after the last one
    private readonly int[][] _previous;
    private readonly int[][] _current;

    private PooledBuffer? _data;
    private int _length;

    // The bit reader: the next bit is the most significant one of _bits
    private int _position;
    private ulong _bits;
    private int _bitCount;
    private bool _afterFF;

    /// <param name="components">The components of the scan, in scan order, with their planes allocated.</param>
    /// <param name="parameters">The coding parameters of the scan.</param>
    /// <param name="interleave">The interleave mode (ILV): 0, 1 or 2.</param>
    /// <param name="restartInterval">The restart interval in lines (see the remarks), or 0.</param>
    /// <param name="scope">The scope charged with the buffered entropy-coded data.</param>
    /// <param name="limits">The limits (for the overflow report).</param>
    /// <param name="cancellationToken">Checked after each line.</param>
    public JpegLsScanDecoder(JpegFrameComponent[] components, JpegLsParameters parameters, int interleave, int restartInterval, AllocationScope scope, ImageResourceLimits limits, CancellationToken cancellationToken)
    {
        _components = components;
        _parameters = parameters;
        _interleave = interleave;
        _restartInterval = restartInterval;
        _scope = scope;
        _limits = limits;
        _cancellationToken = cancellationToken;
        _runIndex = new int[components.Length];
        _previous = new int[components.Length][];
        _current = new int[components.Length][];
        for (var i = 0; i < components.Length; i++)
        {
            _previous[i] = new int[components[i].CodedWidth + 2];
            _current[i] = new int[components[i].CodedWidth + 2];
        }

        ResetContexts();
    }

    /// <summary>Buffers entropy-coded bytes as the container walker delivers them.</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        if (_data is null || _length + data.Length > _data.RawBuffer.Length)
        {
            var capacity = Math.Max(4096, Math.Max(_length + data.Length, (_data?.RawBuffer.Length ?? 0) * 2L));
            if (capacity > CheckedSizes.MaxBufferLength)
            {
                capacity = _length + data.Length;
                if (capacity > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(_limits);
            }

            var buffer = _scope.Rent((int)capacity, AllocationKind.DecoderState, clear: false);
            if (_data is not null)
            {
                _data.RawBuffer.AsSpan(0, _length).CopyTo(buffer.RawBuffer);
                _data.Dispose();
            }

            _data = buffer;
        }

        data.CopyTo(_data.RawBuffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>Decodes the scan at the end of its entropy-coded data, and releases the data.</summary>
    public void Finish()
    {
        try
        {
            switch (_interleave)
            {
                case 0:
                    DecodeComponentLines();
                    break;

                case 1:
                    DecodeInterleavedLines();
                    break;

                default:
                    DecodeInterleavedSamples();
                    break;
            }

            foreach (var component in _components)
            {
                PadPlane(component);
            }
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        _data?.Dispose();
        _data = null;
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Jpeg);

    /// <summary>T.87 A.2.1: A = max(2, (RANGE + 32) / 64), B = C = 0, N = 1, and Nn = 0 for the run interruption contexts; the run indexes are 0.</summary>
    private void ResetContexts()
    {
        _a.AsSpan().Fill(Math.Max(2, (_parameters.Range + 32) >> 6));
        _b.AsSpan().Clear();
        _c.AsSpan().Clear();
        _n.AsSpan().Fill(1);
        _runIndex.AsSpan().Clear();
    }

    /// <summary>At the start of a line of the restart interval: after a complete interval, reads its restart marker and resets the coding.</summary>
    /// <param name="line">The index of the line (of the component, of MCUs or of samples, depending on the interleave mode).</param>
    private void StartLine(int line)
    {
        if (_restartInterval == 0 || line == 0 || line % _restartInterval != 0)
            return;

        // The bits left in the bit buffer pad the interval to a byte boundary; Fill stops at the marker
        _bits = 0;
        _bitCount = 0;
        _afterFF = false;
        var data = _data?.RawBuffer ?? [];
        if (_position + 1 >= _length || data[_position] != 0xFF || data[_position + 1] is < 0xD0 or > 0xD7)
            throw Invalid("A JPEG-LS restart marker is missing.");

        if (data[_position + 1] - 0xD0 != _expectedRestart)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG-LS restart marker RST{data[_position + 1] - 0xD0} is out of sequence (RST{_expectedRestart} expected)."));

        _position += 2;
        _expectedRestart = (_expectedRestart + 1) & 7;
        ResetContexts();
        for (var i = 0; i < _components.Length; i++)
        {
            _previous[i].AsSpan().Clear();
            _current[i].AsSpan().Clear();
        }
    }

    private void DecodeComponentLines()
    {
        var component = _components[0];
        for (var y = 0; y < component.CodedHeight; y++)
        {
            StartLine(y);
            DecodeLine(0, y);
            _cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Line interleaving: <c>V</c> lines of each component in turn per line of MCUs, while a component has lines left.</summary>
    private void DecodeInterleavedLines()
    {
        var mcuRows = 0;
        foreach (var component in _components)
        {
            mcuRows = Math.Max(mcuRows, (component.CodedHeight + component.VerticalSampling - 1) / component.VerticalSampling);
        }

        for (var row = 0; row < mcuRows; row++)
        {
            StartLine(row);
            for (var index = 0; index < _components.Length; index++)
            {
                var component = _components[index];
                for (var line = 0; line < component.VerticalSampling; line++)
                {
                    var y = (row * component.VerticalSampling) + line;
                    if (y < component.CodedHeight)
                    {
                        DecodeLine(index, y);
                    }
                }
            }

            _cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Decodes one line of one component (T.87 A.3 to A.7).</summary>
    private void DecodeLine(int index, int y)
    {
        var previous = _previous[index];
        var current = _current[index];
        var width = _components[index].CodedWidth;
        var near = _parameters.Near;

        // The sample on the left of the line is the first sample of the line above
        current[0] = previous[1];
        var x = 0;
        while (x < width)
        {
            var p = x + 1;
            var ra = current[p - 1];
            var rb = previous[p];
            var rc = previous[p - 1];
            var rd = previous[p + 1];
            var d1 = rd - rb;
            var d2 = rb - rc;
            var d3 = rc - ra;
            if (Math.Abs(d1) <= near && Math.Abs(d2) <= near && Math.Abs(d3) <= near)
            {
                // Run mode (A.7): a run of samples equal to Ra (within NEAR), then an interruption sample unless the run ends the line
                var run = DecodeRun(width - x, ref _runIndex[index]);
                for (var i = 0; i < run; i++)
                {
                    SetSample(current, x + 1 + i, ra);
                }

                x += run;
                if (x == width)
                    break;

                p = x + 1;
                rb = previous[p];
                var runType = Math.Abs(ra - rb) <= near ? 1 : 0;
                var negative = runType == 0 && ra > rb;
                var error = DecodeInterruptionError(runType, _runIndex[index]);
                SetSample(current, p, Reconstruct(runType == 1 ? ra : rb, negative, error));
                if (_runIndex[index] > 0)
                {
                    _runIndex[index]--;
                }
            }
            else
            {
                SetSample(current, p, DecodeRegular(d1, d2, d3, ra, rb, rc));
            }

            x++;
        }

        StoreLine(_components[index], y, current.AsSpan(1, width));
        _previous[index] = current;
        _current[index] = previous;
    }

    /// <summary>Sample interleaving (T.87 B.3.2): the samples of every component in turn, in run mode when every component is.</summary>
    private void DecodeInterleavedSamples()
    {
        var count = _components.Length;
        var width = _components[0].CodedWidth;
        var height = _components[0].CodedHeight;
        var near = _parameters.Near;
        Span<int> ra = stackalloc int[count];
        Span<int> rb = stackalloc int[count];
        Span<int> rc = stackalloc int[count];
        Span<int> rd = stackalloc int[count];
        for (var y = 0; y < height; y++)
        {
            StartLine(y);
            for (var i = 0; i < count; i++)
            {
                _current[i][0] = _previous[i][1];
            }

            var x = 0;
            while (x < width)
            {
                var p = x + 1;
                var run = true;
                for (var i = 0; i < count; i++)
                {
                    ra[i] = _current[i][p - 1];
                    rb[i] = _previous[i][p];
                    rc[i] = _previous[i][p - 1];
                    rd[i] = _previous[i][p + 1];
                    run &= Math.Abs(rd[i] - rb[i]) <= near && Math.Abs(rb[i] - rc[i]) <= near && Math.Abs(rc[i] - ra[i]) <= near;
                }

                if (run)
                {
                    var length = DecodeRun(width - x, ref _runIndex[0]);
                    for (var j = 0; j < length; j++)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            SetSample(_current[i], x + 1 + j, ra[i]);
                        }
                    }

                    x += length;
                    if (x == width)
                        break;

                    // Each component of the interruption sample is coded with the context of RItype 0, from the sample above
                    p = x + 1;
                    for (var i = 0; i < count; i++)
                    {
                        var above = _previous[i][p];
                        var error = DecodeInterruptionError(0, _runIndex[0]);
                        SetSample(_current[i], p, Reconstruct(above, ra[i] > above, error));
                    }

                    if (_runIndex[0] > 0)
                    {
                        _runIndex[0]--;
                    }
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        SetSample(_current[i], p, DecodeRegular(rd[i] - rb[i], rb[i] - rc[i], rc[i] - ra[i], ra[i], rb[i], rc[i]));
                    }
                }

                x++;
            }

            for (var i = 0; i < count; i++)
            {
                StoreLine(_components[i], y, _current[i].AsSpan(1, width));
                (_previous[i], _current[i]) = (_current[i], _previous[i]);
            }

            _cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Writes a reconstructed sample, and repeats it after itself: the sample on the right of the last one of a line is the last one.</summary>
    private static void SetSample(int[] line, int position, int value)
    {
        line[position] = value;
        line[position + 1] = value;
    }

    /// <summary>Decodes a sample in regular mode (T.87 A.3 to A.6).</summary>
    private int DecodeRegular(int d1, int d2, int d3, int ra, int rb, int rc)
    {
        var q1 = QuantizeGradient(d1);
        var q2 = QuantizeGradient(d2);
        var q3 = QuantizeGradient(d3);

        // A.3.4: the sign of the first nonzero quantized gradient makes the context positive
        var negative = q1 < 0 || (q1 == 0 && (q2 < 0 || (q2 == 0 && q3 < 0)));
        var context = negative ? -((81 * q1) + (9 * q2) + q3) : (81 * q1) + (9 * q2) + q3;

        // A.4: median edge detection, corrected by the bias of the context
        int prediction;
        if (rc >= Math.Max(ra, rb))
        {
            prediction = Math.Min(ra, rb);
        }
        else if (rc <= Math.Min(ra, rb))
        {
            prediction = Math.Max(ra, rb);
        }
        else
        {
            prediction = ra + rb - rc;
        }

        prediction = Math.Clamp(negative ? prediction - _c[context] : prediction + _c[context], 0, _parameters.MaxValue);
        var k = GetGolombParameter(_n[context], _a[context]);
        var mapped = DecodeGolomb(k, _parameters.Limit);

        // A.5.2: the inverse of the error mapping, whose two senses are exchanged when the context has a negative bias
        var error = (mapped & 1) != 0 ? -((mapped + 1) >> 1) : mapped >> 1;
        if (_parameters.Near == 0 && k == 0 && 2 * _b[context] <= -_n[context])
        {
            error = -(error + 1);
        }

        UpdateRegular(context, error);
        return Reconstruct(prediction, negative, error);
    }

    /// <summary>Decodes the prediction error of a run interruption sample (T.87 A.7.2) and updates its context.</summary>
    private int DecodeInterruptionError(int runType, int runIndex)
    {
        var context = RegularContexts + runType;
        var n = _n[context];
        var k = GetGolombParameter(n, _a[context] + (runType == 1 ? n >> 1 : 0));
        var mapped = DecodeGolomb(k, _parameters.Limit - RunOrders[runIndex] - 1);
        var value = mapped + runType;
        var error = (value & 1) != 0 ? -((value + 1) >> 1) : value >> 1;
        if (k == 0 && (runType == 1 || mapped != 0) && 2 * _b[context] < n)
        {
            error = -error;
        }

        // A.7.2.2: B counts the negative errors (Nn)
        if (error < 0)
        {
            _b[context]++;
        }

        _a[context] += Math.Abs(error) - runType;
        if (n >= _parameters.Reset)
        {
            _a[context] >>= 1;
            _b[context] >>= 1;
            n >>= 1;
        }

        _n[context] = n + 1;
        return error;
    }

    /// <summary>Updates the statistics and the bias correction of a regular context (T.87 A.6).</summary>
    private void UpdateRegular(int context, int error)
    {
        var b = _b[context] + (error * _parameters.Delta);
        var a = _a[context] + Math.Abs(error);
        var n = _n[context];
        if (n >= _parameters.Reset)
        {
            a >>= 1;
            b >>= 1;
            n >>= 1;
        }

        n++;
        if (b <= -n)
        {
            b += n;
            if (_c[context] > -128)
            {
                _c[context]--;
            }

            if (b <= -n)
            {
                b = -n + 1;
            }
        }
        else if (b > 0)
        {
            b -= n;
            if (_c[context] < 127)
            {
                _c[context]++;
            }

            if (b > 0)
            {
                b = 0;
            }
        }

        _a[context] = a;
        _b[context] = b;
        _n[context] = n;
    }

    /// <summary>The reconstruction of A.4.4: the prediction and the error, modulo the range of the errors, clamped to the samples.</summary>
    private int Reconstruct(int prediction, bool negative, int error)
    {
        var parameters = _parameters;
        var value = negative ? prediction - (error * parameters.Delta) : prediction + (error * parameters.Delta);
        if (value < -parameters.Near)
        {
            value += parameters.Range * parameters.Delta;
        }
        else if (value > parameters.MaxValue + parameters.Near)
        {
            value -= parameters.Range * parameters.Delta;
        }

        return Math.Clamp(value, 0, parameters.MaxValue);
    }

    /// <summary>The quantization of a local gradient of A.3.3, from -4 to 4.</summary>
    private int QuantizeGradient(int gradient)
    {
        var parameters = _parameters;
        if (gradient <= -parameters.T3)
            return -4;

        if (gradient <= -parameters.T2)
            return -3;

        if (gradient <= -parameters.T1)
            return -2;

        if (gradient < -parameters.Near)
            return -1;

        if (gradient <= parameters.Near)
            return 0;

        if (gradient < parameters.T1)
            return 1;

        if (gradient < parameters.T2)
            return 2;

        return gradient < parameters.T3 ? 3 : 4;
    }

    /// <summary>The Golomb parameter of a context: the smallest <c>k</c> such that <c>N * 2^k &gt;= A</c>.</summary>
    private static int GetGolombParameter(int n, long a)
    {
        var k = 0;
        while (((long)n << k) < a)
        {
            k++;
            if (k > 24)
                throw Invalid("The Golomb parameter of a JPEG-LS context is out of range.");
        }

        return k;
    }

    /// <summary>
    /// Decodes a mapped error coded with a limited-length Golomb code (T.87 A.5.3): the quotient in unary, then <c>k</c>
    /// bits, or after <c>limit - qbpp - 1</c> zeros an escape followed by the value minus one in qbpp bits.
    /// </summary>
    private int DecodeGolomb(int k, int limit)
    {
        var parameters = _parameters;
        var escape = limit - parameters.QuantizedBits - 1;
        var zeros = ReadZeros(escape);
        var mapped = zeros == escape ? ReadBits(parameters.QuantizedBits) + 1 : k == 0 ? zeros : (zeros << k) | ReadBits(k);

        // A mapped error codes an error of the range of the samples: a larger one cannot come from a conforming encoder
        if (mapped > 2 * parameters.Range)
            throw Invalid("A JPEG-LS prediction error is out of the range of the samples.");

        return mapped;
    }

    /// <summary>Decodes the length of a run (T.87 A.7.1.2): whole blocks of <c>2^J</c> samples, then the rest of a run that does not end its line.</summary>
    private int DecodeRun(int remaining, ref int runIndex)
    {
        var run = 0;
        while (ReadBit() != 0)
        {
            // A block that the end of the line cuts does not raise the run index
            run += 1 << RunOrders[runIndex];
            if (run <= remaining && runIndex < 31)
            {
                runIndex++;
            }

            if (run >= remaining)
                return remaining;
        }

        var order = RunOrders[runIndex];
        if (order > 0)
        {
            run += ReadBits(order);
        }

        if (run > remaining)
            throw Invalid("A JPEG-LS run goes past the end of its line.");

        return run;
    }

    private static void StoreLine(JpegFrameComponent component, int y, ReadOnlySpan<int> samples)
    {
        var offset = component.GetRowOffset(y);
        if (component.HasDeepSamples)
        {
            var destination = component.DeepSamples.Slice(offset, samples.Length);
            for (var x = 0; x < samples.Length; x++)
            {
                destination[x] = (ushort)samples[x];
            }
        }
        else
        {
            var destination = component.Samples.AsSpan(offset, samples.Length);
            for (var x = 0; x < samples.Length; x++)
            {
                destination[x] = (byte)samples[x];
            }
        }
    }

    /// <summary>
    /// Repeats the last sample of each line and the last line up to the size of the plane: the samples that complete the
    /// MCUs of the other processes, which the upsampling filters may read.
    /// </summary>
    private static void PadPlane(JpegFrameComponent component)
    {
        var width = component.CodedWidth;
        var lines = component.BandRows * ((component.CodedHeight + component.BandRows - 1) / component.BandRows);
        for (var y = 0; y < lines; y++)
        {
            var source = component.GetRowOffset(Math.Min(y, component.CodedHeight - 1));
            var offset = component.GetRowOffset(y);
            if (component.HasDeepSamples)
            {
                var samples = component.DeepSamples;
                if (y >= component.CodedHeight)
                {
                    samples.Slice(source, width).CopyTo(samples[offset..]);
                }

                samples.Slice(offset + width, component.Stride - width).Fill(samples[offset + width - 1]);
            }
            else
            {
                var samples = component.Samples.AsSpan();
                if (y >= component.CodedHeight)
                {
                    samples.Slice(source, width).CopyTo(samples[offset..]);
                }

                samples.Slice(offset + width, component.Stride - width).Fill(samples[offset + width - 1]);
            }
        }
    }

    private int ReadBit() => ReadBits(1);

    private int ReadBits(int count)
    {
        if (_bitCount < count)
        {
            Fill();
            if (_bitCount < count)
                throw Invalid("The JPEG-LS entropy-coded data ends before the last sample of its scan.");
        }

        var value = (int)(_bits >> (64 - count));
        _bits <<= count;
        _bitCount -= count;
        return value;
    }

    /// <summary>Reads the zeros before the next 1 bit and that bit, failing beyond <paramref name="maximum"/> zeros.</summary>
    private int ReadZeros(int maximum)
    {
        var zeros = 0;
        while (true)
        {
            if (_bitCount == 0)
            {
                Fill();
                if (_bitCount == 0)
                    throw Invalid("The JPEG-LS entropy-coded data ends before the last sample of its scan.");
            }

            var leading = Math.Min(BitOperations.LeadingZeroCount(_bits), _bitCount);
            zeros += leading;
            if (zeros > maximum)
                throw Invalid("A JPEG-LS Golomb code is longer than its limit.");

            if (leading < _bitCount)
            {
                _bits <<= leading + 1;
                _bitCount -= leading + 1;
                return zeros;
            }

            _bits = 0;
            _bitCount = 0;
        }
    }

    /// <summary>
    /// Appends bytes to the bit buffer: 8 bits each, 7 for the byte after a byte 0xFF, whose high bit is a stuffed zero. It
    /// stops before a marker (0xFF followed by a byte of which the high bit is set): a restart marker, which
    /// <see cref="StartLine"/> reads, or the end of the data.
    /// </summary>
    private void Fill()
    {
        var data = _data?.RawBuffer ?? [];
        while (_bitCount <= 56 && _position < _length)
        {
            var value = data[_position];
            if (_afterFF)
            {
                if (value >= 0x80)
                    return;

                _bits |= (ulong)value << (64 - 7 - _bitCount);
                _bitCount += 7;
                _afterFF = false;
                _position++;
                continue;
            }

            if (value == 0xFF && _position + 1 < _length && data[_position + 1] >= 0x80)
                return;

            _bits |= (ulong)value << (64 - 8 - _bitCount);
            _bitCount += 8;
            _afterFF = value == 0xFF;
            _position++;
        }
    }
}
