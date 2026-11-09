namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A JPEG-LS test image (ITU-T T.87) defined by the samples of its components, with an independent test-side encoder
/// written from the specification, one sample at a time: the context modeling of the regular mode (A.3 to A.6), the run
/// mode and its interruption samples (A.7), the limited-length Golomb codes (A.5.3), near-lossless quantization, the three
/// interleave modes, preset coding parameters (LSE), restart intervals, the zero bit stuffed after 0xFF, and the HP color
/// transforms of CharLS. The expected pixels are the samples the encoder reconstructs (the source samples when NEAR is 0),
/// formatted by <see cref="JpegLosslessTestImage"/>. Nothing here uses the library decoder.
/// </summary>
internal sealed class JpegLsTestImage
{
    private static readonly int[] RunOrders = [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    private readonly (int H, int V)[] _sampling;
    private readonly int[][] _samples;
    private readonly (int Width, int Height)[] _sizes;
    private int[][] _reconstructed = [];

    private JpegLsTestImage(int width, int height, int precision, (int H, int V)[] sampling, int[][] samples, (int Width, int Height)[] sizes)
    {
        Width = width;
        Height = height;
        Precision = precision;
        _sampling = sampling;
        _samples = samples;
        _sizes = sizes;
    }

    public int Width { get; }

    public int Height { get; }

    public int Precision { get; }

    /// <summary>Creates seeded random samples: noise, smooth areas (runs) and edges, below <paramref name="maxValue"/>.</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public static JpegLsTestImage CreateRandom(int width, int height, (int H, int V)[] sampling, int precision, int seed, int? maxValue = null)
    {
        var random = new Random(seed);
        var max = maxValue ?? ((1 << precision) - 1);
        return FromSamples(width, height, sampling, precision, (component, x, y) =>
        {
            // Flat blocks give runs, gradients give small errors, noise and extremes give large ones
            var block = ((x / 5) + (y / 3) + component) % 4;
            return block switch
            {
                0 => max / 3,
                1 => Math.Clamp(((x * 7) + (y * 3) + (component * 11)) * max / 61, 0, max),
                2 => random.Next(max + 1),
                _ => random.Next(3) switch { 0 => 0, 1 => max, _ => Math.Clamp((max / 2) + random.Next(-2, 3), 0, max) },
            };
        });
    }

    /// <summary>Creates an image from given samples.</summary>
    /// <param name="getSample">Gets a sample: component index, column and line of the component.</param>
    public static JpegLsTestImage FromSamples(int width, int height, (int H, int V)[] sampling, int precision, Func<int, int, int, int> getSample)
    {
        var maxH = sampling.Max(s => s.H);
        var maxV = sampling.Max(s => s.V);
        var samples = new int[sampling.Length][];
        var sizes = new (int, int)[sampling.Length];
        for (var c = 0; c < sampling.Length; c++)
        {
            // The only component of a frame is not subsampled, whatever its sampling factors
            var w = sampling.Length == 1 ? width : ((width * sampling[c].H) + maxH - 1) / maxH;
            var h = sampling.Length == 1 ? height : ((height * sampling[c].V) + maxV - 1) / maxV;
            sizes[c] = (w, h);
            samples[c] = new int[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    samples[c][(y * w) + x] = getSample(c, x, y);
                }
            }
        }

        return new JpegLsTestImage(width, height, precision, sampling, samples, sizes);
    }

    /// <summary>Gets the expected decoded pixels of the last encoding (<see cref="JpegLosslessTestImage.GetExpectedPixels"/>), from the reconstructed samples.</summary>
    public byte[] GetExpectedPixels() => ToLosslessImage(_reconstructed).GetExpectedPixels();

    /// <summary>Gets the pixel format of the decoded image.</summary>
    public PixelFormat PixelFormat => ToLosslessImage(_samples).PixelFormat;

    /// <summary>Encodes the image, and keeps the samples a decoder reconstructs.</summary>
    public byte[] Encode(JpegLsTestOptions? options = null)
    {
        options ??= new JpegLsTestOptions();
        var count = _samples.Length;
        var output = new List<byte> { 0xFF, 0xD8 };
        var coded = _samples;
        if (options.ColorTransform != 0)
        {
            output.AddRange(JpegTestImage.Segment(0xE8, [.. "mrfx"u8, (byte)options.ColorTransform]));
            coded = ForwardColorTransform(options.ColorTransform);
        }

        var frame = new List<byte> { (byte)Precision, (byte)(Height >> 8), (byte)Height, (byte)(Width >> 8), (byte)Width, (byte)count };
        for (var c = 0; c < count; c++)
        {
            frame.AddRange([(byte)(c + 1), (byte)((_sampling[c].H << 4) | _sampling[c].V), 0]);
        }

        output.AddRange(JpegTestImage.Segment(0xF7, [.. frame]));
        if (options.Preset is { } preset)
        {
            output.AddRange(JpegTestImage.Segment(0xF8, [1, .. Word(preset.MaxValue), .. Word(preset.T1), .. Word(preset.T2), .. Word(preset.T3), .. Word(preset.Reset)]));
        }

        if (options.RestartInterval > 0)
        {
            output.AddRange(JpegTestImage.Segment(0xDD, [.. Word(options.RestartInterval)]));
        }

        var parameters = Parameters.Create(Precision, options.Near, options.Preset ?? default);
        var reconstructed = new int[count][];
        var scans = options.Interleave == 0 || count == 1 ? [.. Enumerable.Range(0, count).Select(c => new[] { c })] : new[] { Enumerable.Range(0, count).ToArray() };
        foreach (var scan in scans)
        {
            var header = new List<byte> { (byte)scan.Length };
            foreach (var c in scan)
            {
                header.AddRange([(byte)(c + 1), 0]);
            }

            header.AddRange([(byte)options.Near, (byte)(scan.Length == 1 ? 0 : options.Interleave), 0]);
            output.AddRange(JpegTestImage.Segment(0xDA, [.. header]));
            var encoder = new ScanEncoder(this, parameters, scan, coded, scan.Length == 1 ? 0 : options.Interleave, options.RestartInterval, output);
            encoder.Encode();
            foreach (var c in scan)
            {
                reconstructed[c] = encoder.Reconstructed[c]!;
            }
        }

        output.AddRange([0xFF, 0xD9]);
        _reconstructed = options.ColorTransform != 0 ? InverseColorTransform(options.ColorTransform, reconstructed) : reconstructed;
        return [.. output];
    }

    private static byte[] Word(int value) => [(byte)(value >> 8), (byte)value];

    private JpegLosslessTestImage ToLosslessImage(int[][] planes)
        => JpegLosslessTestImage.FromSamples(Width, Height, _sampling, Precision, rgb: planes.Length == 3, (c, x, y) => planes[c][(y * _sizes[c].Width) + x]);

    /// <summary>The HP color transforms of CharLS (8-bit samples, modulo 256).</summary>
    private int[][] ForwardColorTransform(int transform)
    {
        var (r, g, b) = (_samples[0], _samples[1], _samples[2]);
        var result = new int[3][] { new int[r.Length], new int[r.Length], new int[r.Length] };
        for (var i = 0; i < r.Length; i++)
        {
            switch (transform)
            {
                case 1:
                    (result[0][i], result[1][i], result[2][i]) = ((r[i] - g[i] + 128) & 0xFF, g[i], (b[i] - g[i] + 128) & 0xFF);
                    break;

                case 2:
                    (result[0][i], result[1][i], result[2][i]) = ((r[i] - g[i] + 128) & 0xFF, g[i], (b[i] - ((r[i] + g[i]) >> 1) + 128) & 0xFF);
                    break;

                default:
                    var v2 = (b[i] - g[i] + 128) & 0xFF;
                    var v3 = (r[i] - g[i] + 128) & 0xFF;
                    (result[0][i], result[1][i], result[2][i]) = ((g[i] + ((v2 + v3) >> 2) - 64) & 0xFF, v2, v3);
                    break;
            }
        }

        return result;
    }

    private static int[][] InverseColorTransform(int transform, int[][] planes)
    {
        var length = planes[0].Length;
        var result = new int[3][] { new int[length], new int[length], new int[length] };
        for (var i = 0; i < length; i++)
        {
            var (v1, v2, v3) = (planes[0][i], planes[1][i], planes[2][i]);
            switch (transform)
            {
                case 1:
                    (result[0][i], result[1][i], result[2][i]) = ((v1 + v2 - 128) & 0xFF, v2, (v3 + v2 - 128) & 0xFF);
                    break;

                case 2:
                    var red = (v1 + v2 - 128) & 0xFF;
                    (result[0][i], result[1][i], result[2][i]) = (red, v2, (v3 + ((red + v2) >> 1) - 128) & 0xFF);
                    break;

                default:
                    var green = v1 - ((v3 + v2) >> 2) + 64;
                    (result[0][i], result[1][i], result[2][i]) = ((v3 + green - 128) & 0xFF, green & 0xFF, (v2 + green - 128) & 0xFF);
                    break;
            }
        }

        return result;
    }

    /// <summary>The coding parameters of T.87 C.2.4.1.1 and A.2.1.</summary>
    private sealed record Parameters(int MaxValue, int Near, int T1, int T2, int T3, int Reset)
    {
        public int Delta => (2 * Near) + 1;

        public int Range => Near == 0 ? MaxValue + 1 : ((MaxValue + (2 * Near)) / Delta) + 1;

        public int QuantizedBits => BitsFor(Range);

        public int Limit
        {
            get
            {
                var bits = Math.Max(2, BitsFor(MaxValue + 1));
                return 2 * (bits + Math.Max(8, bits));
            }
        }

        public static Parameters Create(int precision, int near, (int MaxValue, int T1, int T2, int T3, int Reset) preset)
        {
            var max = preset.MaxValue == 0 ? (1 << precision) - 1 : preset.MaxValue;
            int t1, t2, t3;
            if (max >= 128)
            {
                var factor = (Math.Min(max, 4095) + 128) / 256;
                t1 = Clamp((factor * 1) + 2 + (3 * near), near + 1, max);
                t2 = Clamp((factor * 4) + 3 + (5 * near), t1, max);
                t3 = Clamp((factor * 17) + 4 + (7 * near), t2, max);
            }
            else
            {
                var factor = 256 / (max + 1);
                t1 = Clamp(Math.Max(2, (3 / factor) + (3 * near)), near + 1, max);
                t2 = Clamp(Math.Max(3, (7 / factor) + (5 * near)), t1, max);
                t3 = Clamp(Math.Max(4, (21 / factor) + (7 * near)), t2, max);
            }

            return new Parameters(max, near, preset.T1 != 0 ? preset.T1 : t1, preset.T2 != 0 ? preset.T2 : t2, preset.T3 != 0 ? preset.T3 : t3, preset.Reset != 0 ? preset.Reset : 64);
        }

        private static int Clamp(int value, int minimum, int max) => value > max || value < minimum ? minimum : value;

        private static int BitsFor(int count)
        {
            var bits = 0;
            while (1 << bits < count)
            {
                bits++;
            }

            return bits;
        }
    }

    /// <summary>The encoding of one scan, one sample at a time.</summary>
    private sealed class ScanEncoder
    {
        private readonly JpegLsTestImage _image;
        private readonly Parameters _parameters;
        private readonly int[] _scan;
        private readonly int[][] _source;
        private readonly int _interleave;
        private readonly int _restartInterval;
        private readonly List<byte> _output;
        private readonly long[] _a = new long[367];
        private readonly int[] _b = new int[367];
        private readonly int[] _c = new int[365];
        private readonly int[] _n = new int[367];
        private readonly int[] _runIndex;

        // The previous and current reconstructed lines of each component of the scan, with a sample on each side
        private readonly int[][] _previous;
        private readonly int[][] _current;

        // The bits not yet written, most significant first, and whether the last byte written was 0xFF
        private long _bits;
        private int _bitCount;
        private bool _afterFF;
        private int _restartIndex;

        public ScanEncoder(JpegLsTestImage image, Parameters parameters, int[] scan, int[][] source, int interleave, int restartInterval, List<byte> output)
        {
            _image = image;
            _parameters = parameters;
            _scan = scan;
            _source = source;
            _interleave = interleave;
            _restartInterval = restartInterval;
            _output = output;
            _runIndex = new int[scan.Length];
            _previous = new int[scan.Length][];
            _current = new int[scan.Length][];
            Reconstructed = new int[image._samples.Length][];
            for (var i = 0; i < scan.Length; i++)
            {
                var width = image._sizes[scan[i]].Width;
                _previous[i] = new int[width + 2];
                _current[i] = new int[width + 2];
                Reconstructed[scan[i]] = new int[image._samples[scan[i]].Length];
            }

            Reset();
        }

        public int[]?[] Reconstructed { get; }

        public void Encode()
        {
            if (_interleave == 2)
            {
                var (width, height) = _image._sizes[_scan[0]];
                for (var y = 0; y < height; y++)
                {
                    StartLine(y);
                    EncodeSampleLine(width, y);
                }
            }
            else if (_interleave == 1)
            {
                var rows = _scan.Max(c => (_image._sizes[c].Height + _image._sampling[c].V - 1) / _image._sampling[c].V);
                for (var row = 0; row < rows; row++)
                {
                    StartLine(row);
                    for (var i = 0; i < _scan.Length; i++)
                    {
                        var v = _image._sampling[_scan[i]].V;
                        for (var line = 0; line < v; line++)
                        {
                            var y = (row * v) + line;
                            if (y < _image._sizes[_scan[i]].Height)
                            {
                                EncodeLine(i, y);
                            }
                        }
                    }
                }
            }
            else
            {
                for (var y = 0; y < _image._sizes[_scan[0]].Height; y++)
                {
                    StartLine(y);
                    EncodeLine(0, y);
                }
            }

            FlushBits();
        }

        private void Reset()
        {
            Array.Fill(_a, Math.Max(2, (_parameters.Range + 32) / 64));
            Array.Clear(_b);
            Array.Clear(_c);
            Array.Fill(_n, 1);
            Array.Clear(_runIndex);
            foreach (var line in _previous.Concat(_current))
            {
                Array.Clear(line);
            }
        }

        private void StartLine(int line)
        {
            if (_restartInterval == 0 || line == 0 || line % _restartInterval != 0)
                return;

            FlushBits();
            _output.AddRange([0xFF, (byte)(0xD0 + (_restartIndex & 7))]);
            _restartIndex++;
            Reset();
        }

        private void EncodeLine(int i, int y)
        {
            var component = _scan[i];
            var width = _image._sizes[component].Width;
            var source = _source[component];
            var previous = _previous[i];
            var current = _current[i];
            var near = _parameters.Near;
            current[0] = previous[1];
            var x = 0;
            while (x < width)
            {
                var p = x + 1;
                var ra = current[p - 1];
                var rb = previous[p];
                var rc = previous[p - 1];
                var rd = previous[p + 1];
                if (Math.Abs(rd - rb) <= near && Math.Abs(rb - rc) <= near && Math.Abs(rc - ra) <= near)
                {
                    var run = 0;
                    while (x + run < width && Math.Abs(source[(y * width) + x + run] - ra) <= near)
                    {
                        Set(current, x + 1 + run, ra);
                        run++;
                    }

                    WriteRun(run, x + run == width, ref _runIndex[i]);
                    x += run;
                    if (x == width)
                        break;

                    p = x + 1;
                    rb = previous[p];
                    var runType = Math.Abs(ra - rb) <= near ? 1 : 0;
                    var negative = runType == 0 && ra > rb;
                    Set(current, p, WriteInterruption(source[(y * width) + x], runType == 1 ? ra : rb, negative, runType, _runIndex[i]));
                    if (_runIndex[i] > 0)
                    {
                        _runIndex[i]--;
                    }
                }
                else
                {
                    Set(current, p, WriteRegular(source[(y * width) + x], ra, rb, rc, rd));
                }

                x++;
            }

            Array.Copy(current, 1, Reconstructed[component]!, y * width, width);
            (_previous[i], _current[i]) = (current, previous);
        }

        private void EncodeSampleLine(int width, int y)
        {
            var count = _scan.Length;
            var near = _parameters.Near;
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
                    var (ra, rb, rc, rd) = (_current[i][p - 1], _previous[i][p], _previous[i][p - 1], _previous[i][p + 1]);
                    run &= Math.Abs(rd - rb) <= near && Math.Abs(rb - rc) <= near && Math.Abs(rc - ra) <= near;
                }

                if (run)
                {
                    var values = Enumerable.Range(0, count).Select(i => _current[i][p - 1]).ToArray();
                    var length = 0;
                    while (x + length < width && Enumerable.Range(0, count).All(i => Math.Abs(_source[_scan[i]][(y * width) + x + length] - values[i]) <= near))
                    {
                        for (var i = 0; i < count; i++)
                        {
                            Set(_current[i], x + 1 + length, values[i]);
                        }

                        length++;
                    }

                    WriteRun(length, x + length == width, ref _runIndex[0]);
                    x += length;
                    if (x == width)
                        break;

                    p = x + 1;
                    for (var i = 0; i < count; i++)
                    {
                        var above = _previous[i][p];
                        Set(_current[i], p, WriteInterruption(_source[_scan[i]][(y * width) + x], above, values[i] > above, 0, _runIndex[0]));
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
                        var (ra, rb, rc, rd) = (_current[i][p - 1], _previous[i][p], _previous[i][p - 1], _previous[i][p + 1]);
                        Set(_current[i], p, WriteRegular(_source[_scan[i]][(y * width) + x], ra, rb, rc, rd));
                    }
                }

                x++;
            }

            for (var i = 0; i < count; i++)
            {
                Array.Copy(_current[i], 1, Reconstructed[_scan[i]]!, y * width, width);
                (_previous[i], _current[i]) = (_current[i], _previous[i]);
            }
        }

        private static void Set(int[] line, int position, int value)
        {
            line[position] = value;
            line[position + 1] = value;
        }

        private int WriteRegular(int sample, int ra, int rb, int rc, int rd)
        {
            var q1 = QuantizeGradient(rd - rb);
            var q2 = QuantizeGradient(rb - rc);
            var q3 = QuantizeGradient(rc - ra);
            var negative = q1 < 0 || (q1 == 0 && (q2 < 0 || (q2 == 0 && q3 < 0)));
            var context = Math.Abs((81 * q1) + (9 * q2) + q3);
            var prediction = rc >= Math.Max(ra, rb) ? Math.Min(ra, rb) : rc <= Math.Min(ra, rb) ? Math.Max(ra, rb) : ra + rb - rc;
            prediction = Math.Clamp(negative ? prediction - _c[context] : prediction + _c[context], 0, _parameters.MaxValue);
            var error = QuantizeError(negative ? prediction - sample : sample - prediction);
            var reconstructed = Reconstruct(prediction, negative, error);
            var k = GolombParameter(_n[context], _a[context]);
            var special = _parameters.Near == 0 && k == 0 && 2 * _b[context] <= -_n[context];
            var mapped = special
                ? error >= 0 ? (2 * error) + 1 : -2 * (error + 1)
                : error >= 0 ? 2 * error : (-2 * error) - 1;
            WriteGolomb(mapped, k, _parameters.Limit);

            // A.6: the statistics and the bias correction
            var b = _b[context] + (error * _parameters.Delta);
            var a = _a[context] + Math.Abs(error);
            var n = _n[context];
            if (n == _parameters.Reset)
            {
                a >>= 1;
                b >>= 1;
                n >>= 1;
            }

            n++;
            if (b <= -n)
            {
                b += n;
                _c[context] = Math.Max(-128, _c[context] - 1);
                b = Math.Max(b, -n + 1);
            }
            else if (b > 0)
            {
                b -= n;
                _c[context] = Math.Min(127, _c[context] + 1);
                b = Math.Min(b, 0);
            }

            (_a[context], _b[context], _n[context]) = (a, b, n);
            return reconstructed;
        }

        private int WriteInterruption(int sample, int prediction, bool negative, int runType, int runIndex)
        {
            var context = 365 + runType;
            var error = QuantizeError(negative ? prediction - sample : sample - prediction);
            var reconstructed = Reconstruct(prediction, negative, error);
            var n = _n[context];
            var k = GolombParameter(n, _a[context] + (runType == 1 ? n >> 1 : 0));

            // A.7.2.1: the mapping, with the senses exchanged when k is 0 and the errors are mostly negative
            var exchange = k == 0 && error != 0 && 2 * _b[context] < n;
            var mapped = (error < 0 ? (-2 * error) - 1 - (exchange ? -1 : 0) : (2 * error) + (exchange ? -1 : 0)) - runType;
            WriteGolomb(mapped, k, _parameters.Limit - RunOrders[runIndex] - 1);
            if (error < 0)
            {
                _b[context]++;
            }

            _a[context] += ((mapped + 1 - runType) >> 1);
            if (n == _parameters.Reset)
            {
                _a[context] >>= 1;
                _b[context] >>= 1;
                n >>= 1;
            }

            _n[context] = n + 1;
            return reconstructed;
        }

        /// <summary>The quantization of a prediction error (A.4.4) and its reduction modulo RANGE (A.4.5).</summary>
        private int QuantizeError(int error)
        {
            var near = _parameters.Near;
            if (near > 0)
            {
                error = error > 0 ? (near + error) / _parameters.Delta : -(near - error) / _parameters.Delta;
            }

            var range = _parameters.Range;
            var maxError = (range + 1) / 2;
            if (error < maxError - range)
            {
                error += range;
            }
            else if (error >= maxError)
            {
                error -= range;
            }

            return error;
        }

        private int Reconstruct(int prediction, bool negative, int error)
        {
            var value = negative ? prediction - (error * _parameters.Delta) : prediction + (error * _parameters.Delta);
            if (value < -_parameters.Near)
            {
                value += _parameters.Range * _parameters.Delta;
            }
            else if (value > _parameters.MaxValue + _parameters.Near)
            {
                value -= _parameters.Range * _parameters.Delta;
            }

            return Math.Clamp(value, 0, _parameters.MaxValue);
        }

        private int QuantizeGradient(int gradient)
        {
            var (near, t1, t2, t3) = (_parameters.Near, _parameters.T1, _parameters.T2, _parameters.T3);
            return gradient <= -t3 ? -4 : gradient <= -t2 ? -3 : gradient <= -t1 ? -2 : gradient < -near ? -1 : gradient <= near ? 0 : gradient < t1 ? 1 : gradient < t2 ? 2 : gradient < t3 ? 3 : 4;
        }

        private static int GolombParameter(int n, long a)
        {
            var k = 0;
            while (((long)n << k) < a)
            {
                k++;
            }

            return k;
        }

        /// <summary>A.7.1.1: whole blocks of 2^J samples coded by a 1 each, then a 0 and the rest of the run in J bits unless the run ends the line.</summary>
        private void WriteRun(int run, bool endOfLine, ref int runIndex)
        {
            while (run >= 1 << RunOrders[runIndex])
            {
                WriteBits(1, 1);
                run -= 1 << RunOrders[runIndex];
                if (runIndex < 31)
                {
                    runIndex++;
                }
            }

            if (endOfLine)
            {
                if (run > 0)
                {
                    WriteBits(1, 1);
                }
            }
            else
            {
                WriteBits(0, 1);
                WriteBits(run, RunOrders[runIndex]);
            }
        }

        /// <summary>A.5.3: the limited-length Golomb code of a mapped error.</summary>
        private void WriteGolomb(int mapped, int k, int limit)
        {
            var escape = limit - _parameters.QuantizedBits - 1;
            var quotient = mapped >> k;
            if (quotient < escape)
            {
                WriteZeros(quotient);
                WriteBits(1, 1);
                WriteBits(mapped & ((1 << k) - 1), k);
            }
            else
            {
                WriteZeros(escape);
                WriteBits(1, 1);
                WriteBits(mapped - 1, _parameters.QuantizedBits);
            }
        }

        private void WriteZeros(int count)
        {
            for (; count > 16; count -= 16)
            {
                WriteBits(0, 16);
            }

            WriteBits(0, count);
        }

        private void WriteBits(int value, int count)
        {
            if (count == 0)
                return;

            _bits = (_bits << count) | (uint)(value & ((1 << count) - 1));
            _bitCount += count;
            EmitBytes();
        }

        /// <summary>Writes whole bytes: 8 bits, or 7 after a byte 0xFF (whose next byte starts with a stuffed zero bit).</summary>
        private void EmitBytes()
        {
            while (true)
            {
                var width = _afterFF ? 7 : 8;
                if (_bitCount < width)
                    return;

                var value = (int)((_bits >> (_bitCount - width)) & ((1 << width) - 1));
                _bitCount -= width;
                _output.Add((byte)value);
                _afterFF = value == 0xFF;
            }
        }

        /// <summary>Pads the last byte with zero bits, and ends a byte 0xFF with a zero byte so that a marker can follow.</summary>
        private void FlushBits()
        {
            var width = _afterFF ? 7 : 8;
            if (_bitCount > 0)
            {
                WriteBits(0, width - _bitCount);
            }

            if (_afterFF)
            {
                _output.Add(0);
                _afterFF = false;
            }

            _bits = 0;
            _bitCount = 0;
        }
    }
}
