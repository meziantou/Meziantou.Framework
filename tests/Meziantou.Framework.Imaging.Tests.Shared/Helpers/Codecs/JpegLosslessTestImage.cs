using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A lossless JPEG test image (ITU-T T.81 Annex H) defined by the samples of its components, with an independent
/// test-side encoder written from the specification, one sample at a time: the prediction of H.1.2.1 (the first line of a
/// scan and of a restart interval, the first sample of the other lines, the seven predictors), the difference modulo 2^16
/// and its Huffman coding (table H.2), the point transform, interleaved and non-interleaved scans with the samples that
/// complete the MCUs, and restart intervals. The expected pixels are the samples themselves, upsampled and converted by
/// the contract of the library transcribed in <see cref="JpegTestImage"/>, and scaled to 16 bits when the precision is
/// not 8 bits. Nothing here uses the library decoder.
/// </summary>
internal sealed class JpegLosslessTestImage
{
    private JpegLosslessTestImage(int width, int height, int precision, bool rgb, Component[] components)
    {
        Width = width;
        Height = height;
        Precision = precision;
        Rgb = rgb;
        Components = components;
        MaxHorizontal = components.Max(component => component.H);
        MaxVertical = components.Max(component => component.V);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the number of bits of a sample: 2 to 16.</summary>
    public int Precision { get; }

    /// <summary>Gets the largest sample value.</summary>
    public int MaxSample => (1 << Precision) - 1;

    /// <summary>Gets a value indicating whether three components are RGB samples (Adobe APP14 transform 0) rather than YCbCr ones (JFIF).</summary>
    public bool Rgb { get; }

    public Component[] Components { get; }

    public int MaxHorizontal { get; }

    public int MaxVertical { get; }

    /// <summary>Gets the pixel format of the decoded image: the samples that do not have 8 bits are scaled to 16 bits, and color ones are then opaque RGBA.</summary>
    public PixelFormat PixelFormat => Precision != 8
        ? Components.Length == 1 ? PixelFormat.Gray16 : PixelFormat.Rgba64
        : Components.Length == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24;

    /// <summary>Gets a value indicating whether the last encoding wrote the difference 32,768, which has a category of its own and no additional bit.</summary>
    public bool WroteLargestDifference { get; private set; }

    /// <summary>Creates seeded random samples: noise, a gradient or the extreme values, in turn.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="sampling">The sampling factors of each component (1, 3 or 4 components).</param>
    /// <param name="precision">The number of bits of a sample: 2 to 16.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="rgb">RGB samples instead of YCbCr (3 components).</param>
    /// <param name="pointTransform">The number of low bits of every sample that are zero, so that a scan with this point transform codes the samples exactly.</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public static JpegLosslessTestImage CreateRandom(int width, int height, (int H, int V)[] sampling, int precision, int seed, bool rgb = false, int pointTransform = 0)
    {
        var random = new Random(seed);
        var max = (1 << precision) - 1;
        return FromSamples(width, height, sampling, precision, rgb, (component, x, y) =>
        {
            var value = ((seed + component) % 3) switch
            {
                0 => random.Next(max + 1),
                1 => Math.Clamp((((x * 5) + (y * 3) + (component * 7)) * max / 97) + random.Next(-2, 3), 0, max),
                _ => random.Next(4) switch { 0 => 0, 1 => max, 2 => max / 2, _ => (max / 2) + 1 },
            };

            return (value >> pointTransform) << pointTransform;
        });
    }

    /// <summary>Creates an image from given samples.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="sampling">The sampling factors of each component (1, 3 or 4 components).</param>
    /// <param name="precision">The number of bits of a sample: 2 to 16.</param>
    /// <param name="rgb">RGB samples instead of YCbCr (3 components).</param>
    /// <param name="getSample">Gets a sample: component index, column and line of the component.</param>
    public static JpegLosslessTestImage FromSamples(int width, int height, (int H, int V)[] sampling, int precision, bool rgb, Func<int, int, int, int> getSample)
    {
        // The sampling factors of the only component of a frame play no role
        var maxH = sampling.Length == 1 ? 1 : sampling.Max(s => s.H);
        var maxV = sampling.Length == 1 ? 1 : sampling.Max(s => s.V);
        var components = new Component[sampling.Length];
        for (var c = 0; c < sampling.Length; c++)
        {
            var h = sampling.Length == 1 ? 1 : sampling[c].H;
            var v = sampling.Length == 1 ? 1 : sampling[c].V;
            var id = rgb ? (byte)"RGB"[c] : (byte)(c + 1);
            var component = new Component(id, h, v, sampling[c], ((width * h) + maxH - 1) / maxH, ((height * v) + maxV - 1) / maxV);
            for (var y = 0; y < component.Height; y++)
            {
                for (var x = 0; x < component.Width; x++)
                {
                    component.Samples[(y * component.Width) + x] = getSample(c, x, y);
                }
            }

            components[c] = component;
        }

        return new JpegLosslessTestImage(width, height, precision, rgb, components);
    }

    /// <summary>Gets the samples of every pixel, interleaved, at the precision of the image: the stored samples, upsampled to the size of the image.</summary>
    public int[] GetStoredSamples()
    {
        var channels = Components.Length;
        var values = new int[Width * Height * channels];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var component = Components[c];
                    values[(((y * Width) + x) * channels) + c] = JpegTestImage.Upsample(component.Samples, component.Width, component.Height, MaxHorizontal / component.H, MaxVertical / component.V, x, y);
                }
            }
        }

        return values;
    }

    /// <summary>Gets the samples of every pixel, interleaved, at the precision of the image: gray, or red, green and blue (converted from YCbCr unless the samples are RGB).</summary>
    public int[] GetExpectedSamples()
    {
        if (Components.Length == 4)
            throw new InvalidOperationException("The pixels of inks depend on a color conversion: use GetStoredSamples.");

        var values = GetStoredSamples();
        if (Components.Length == 3 && !Rgb)
        {
            for (var offset = 0; offset < values.Length; offset += 3)
            {
                (values[offset], values[offset + 1], values[offset + 2]) = JpegTestImage.ConvertYCbCr(values[offset], values[offset + 1], values[offset + 2], Precision);
            }
        }

        return values;
    }

    /// <summary>
    /// Gets the expected decoded pixels in <see cref="PixelFormat"/>: <see cref="PixelFormat.Gray8"/> or
    /// <see cref="PixelFormat.Rgb24"/> bytes for 8-bit samples; otherwise the native 16-bit samples of
    /// <see cref="PixelFormat.Gray16"/> or opaque <see cref="PixelFormat.Rgba64"/>, each scaled to 16 bits.
    /// </summary>
    public byte[] GetExpectedPixels()
    {
        var values = GetExpectedSamples();
        if (Precision == 8)
            return [.. values.Select(value => (byte)value)];

        var channels = Components.Length;
        var samples = new ushort[Width * Height * (channels == 1 ? 1 : 4)];
        for (var pixel = 0; pixel < Width * Height; pixel++)
        {
            if (channels == 1)
            {
                samples[pixel] = ScaleToSixteenBits(values[pixel]);
                continue;
            }

            samples[4 * pixel] = ScaleToSixteenBits(values[3 * pixel]);
            samples[(4 * pixel) + 1] = ScaleToSixteenBits(values[(3 * pixel) + 1]);
            samples[(4 * pixel) + 2] = ScaleToSixteenBits(values[(3 * pixel) + 2]);
            samples[(4 * pixel) + 3] = ushort.MaxValue;
        }

        return unsafe(MemoryMarshal.AsBytes(samples.AsSpan())).ToArray();
    }

    /// <summary>The nearest 16-bit value of the same fraction of the range.</summary>
    public ushort ScaleToSixteenBits(int sample) => (ushort)Math.Floor((sample * 65535.0 / MaxSample) + 0.5);

    /// <summary>Encodes the image.</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public byte[] Encode(JpegLosslessTestOptions? options = null)
    {
        options ??= new JpegLosslessTestOptions();
        WroteLargestDifference = false;
        var output = new List<byte> { 0xFF, 0xD8 };
        if (Rgb || Components.Length == 4)
        {
            output.AddRange(JpegTestImage.Segment(0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 0]));
        }
        else
        {
            output.AddRange(JpegTestImage.Segment(0xE0, [.. "JFIF\0"u8, 1, 2, 0, 0, 1, 0, 1, 0, 0]));
        }

        foreach (var segment in options.SegmentsBeforeFrame)
        {
            output.AddRange(segment);
        }

        // No quantization table: the selector of every component is zero (T.81 table B.2)
        var frame = new List<byte> { (byte)Precision, (byte)(Height >> 8), (byte)Height, (byte)(Width >> 8), (byte)Width, (byte)Components.Length };
        foreach (var component in Components)
        {
            frame.AddRange([component.Id, (byte)((component.Declared.H << 4) | component.Declared.V), 0]);
        }

        output.AddRange(JpegTestImage.Segment(options.FrameMarker, [.. frame]));
        var tableCount = options.TablePerComponent ? Components.Length : Math.Min(2, Components.Length);
        var tables = new HuffmanCode[tableCount];
        for (var t = 0; t < tableCount; t++)
        {
            tables[t] = HuffmanCode.Create(options.Huffman, options.HuffmanSeed + t);
            output.AddRange(JpegTestImage.Segment(0xC4, tables[t].ToDefinition((byte)t)));
        }

        if (options.RestartInterval > 0 && options.RestartRows == 0)
        {
            output.AddRange(JpegTestImage.Segment(0xDD, [(byte)(options.RestartInterval >> 8), (byte)options.RestartInterval]));
        }

        var scans = options.Scans ?? [new JpegLosslessTestScan([.. Enumerable.Range(0, Components.Length)], options.Predictor, options.PointTransform)];
        var padding = options.PaddingSeed is { } seed ? new Random(seed) : null;
        foreach (var scan in scans)
        {
            var interleaved = scan.Components.Length > 1;
            var mcusX = interleaved ? (Width + MaxHorizontal - 1) / MaxHorizontal : Components[scan.Components[0]].Width;
            var mcusY = interleaved ? (Height + MaxVertical - 1) / MaxVertical : Components[scan.Components[0]].Height;
            var restartInterval = options.RestartRows > 0 ? options.RestartRows * mcusX : options.RestartInterval;
            if (options.RestartRows > 0)
            {
                output.AddRange(JpegTestImage.Segment(0xDD, [(byte)(restartInterval >> 8), (byte)restartInterval]));
            }

            var header = new List<byte> { (byte)scan.Components.Length };
            foreach (var index in scan.Components)
            {
                // The DC table of the component; the AC table selector is zero
                header.AddRange([Components[index].Id, (byte)(GetTable(index, tableCount) << 4)]);
            }

            header.AddRange([(byte)scan.Predictor, (byte)options.UnusedScanParameters.End, (byte)((options.UnusedScanParameters.High << 4) | scan.PointTransform)]);
            output.AddRange(JpegTestImage.Segment(0xDA, [.. header]));
            EncodeScan(output, scan, tables, tableCount, mcusX, mcusY, restartInterval, padding);
        }

        output.AddRange([0xFF, 0xD9]);
        return [.. output];
    }

    private static int GetTable(int component, int tableCount) => Math.Min(component, tableCount - 1);

    /// <summary>Writes the entropy-coded data of one scan, one sample at a time (T.81 H.1.2).</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private void EncodeScan(List<byte> output, JpegLosslessTestScan scan, HuffmanCode[] tables, int tableCount, int mcusX, int mcusY, int restartInterval, Random? padding)
    {
        var interleaved = scan.Components.Length > 1;
        var pointTransform = scan.PointTransform;
        var codedMax = MaxSample >> pointTransform;

        // The samples as they are coded (divided by the point transform), with the ones that complete the MCUs
        var planes = new Dictionary<int, (int[] Values, int Columns, int Lines, int H, int V)>();
        foreach (var index in scan.Components)
        {
            var component = Components[index];
            var h = interleaved ? component.H : 1;
            var v = interleaved ? component.V : 1;
            var columns = mcusX * h;
            var lines = mcusY * v;
            var values = new int[columns * lines];
            for (var y = 0; y < lines; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    if (x < component.Width && y < component.Height)
                    {
                        var sample = component.Samples[(y * component.Width) + x];
                        if ((sample >> pointTransform) << pointTransform != sample)
                            throw new InvalidOperationException("The point transform of the scan would lose bits of a sample.");

                        values[(y * columns) + x] = sample >> pointTransform;
                    }
                    else
                    {
                        values[(y * columns) + x] = padding is not null
                            ? padding.Next(codedMax + 1)
                            : component.Samples[(Math.Min(y, component.Height - 1) * component.Width) + Math.Min(x, component.Width - 1)] >> pointTransform;
                    }
                }
            }

            planes[index] = (values, columns, lines, h, v);
        }

        var codes = new List<(int Bits, int Length)>();
        var firstLine = scan.Components.ToDictionary(index => index, _ => 0);
        var restartIndex = 0;
        for (var mcu = 0; mcu < mcusX * mcusY; mcu++)
        {
            var mx = mcu % mcusX;
            var my = mcu / mcusX;
            if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
            {
                // The prediction is reset: the line of this MCU is predicted like the first one of the scan
                output.AddRange(JpegTestImage.PackBits(codes));
                codes.Clear();
                output.AddRange([0xFF, (byte)(0xD0 + (restartIndex & 7))]);
                restartIndex++;
                foreach (var index in scan.Components)
                {
                    firstLine[index] = my * planes[index].V;
                }
            }

            foreach (var index in scan.Components)
            {
                var (values, columns, _, h, v) = planes[index];
                var table = tables[GetTable(index, tableCount)];
                for (var by = 0; by < v; by++)
                {
                    for (var bx = 0; bx < h; bx++)
                    {
                        var x = (mx * h) + bx;
                        var y = (my * v) + by;
                        int prediction;
                        if (y == firstLine[index])
                        {
                            prediction = x == 0 ? 1 << (Precision - pointTransform - 1) : values[(y * columns) + x - 1];
                        }
                        else if (x == 0)
                        {
                            prediction = values[((y - 1) * columns) + x];
                        }
                        else
                        {
                            var ra = values[(y * columns) + x - 1];
                            var rb = values[((y - 1) * columns) + x];
                            var rc = values[((y - 1) * columns) + x - 1];
                            prediction = scan.Predictor switch
                            {
                                1 => ra,
                                2 => rb,
                                3 => rc,
                                4 => ra + rb - rc,
                                5 => ra + ((rb - rc) >> 1),
                                6 => rb + ((ra - rc) >> 1),
                                7 => (ra + rb) / 2,

                                // Not a predictor: the decoder must reject the scan before its data
                                _ => 0,
                            };
                        }

                        WriteDifference(codes, table, values[(y * columns) + x] - prediction);
                    }
                }
            }
        }

        output.AddRange(JpegTestImage.PackBits(codes));
    }

    /// <summary>Writes a difference modulo 2^16: its category, then the bits that select the value in the category (T.81 table H.2).</summary>
    private void WriteDifference(List<(int Bits, int Length)> codes, HuffmanCode table, int difference)
    {
        // The representative of the difference modulo 2^16 between -32767 and 32768
        var value = ((difference % 65536) + 65536) % 65536;
        if (value > 32768)
        {
            value -= 65536;
        }

        if (value == 32768)
        {
            // No additional bit
            WroteLargestDifference = true;
            codes.Add(table.GetCode(16));
            return;
        }

        var category = 0;
        for (var magnitude = Math.Abs(value); magnitude > 0; magnitude >>= 1)
        {
            category++;
        }

        codes.Add(table.GetCode(category));
        if (category > 0)
        {
            // A negative difference is coded as the low bits of the difference minus one
            codes.Add(((value < 0 ? value - 1 : value) & ((1 << category) - 1), category));
        }
    }

    /// <summary>A component: its sampling factors and its samples.</summary>
    internal sealed class Component
    {
        public Component(byte id, int h, int v, (int H, int V) declared, int width, int height)
        {
            Id = id;
            H = h;
            V = v;
            Declared = declared;
            Width = width;
            Height = height;
            Samples = new int[width * height];
        }

        public byte Id { get; }

        public int H { get; }

        public int V { get; }

        /// <summary>Gets the sampling factors written in the frame header (those of the only component of a frame play no role).</summary>
        public (int H, int V) Declared { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Gets the samples, <see cref="Width"/> per line.</summary>
        public int[] Samples { get; }
    }

    /// <summary>A Huffman code of the seventeen difference categories (canonical codes, T.81 Annex C).</summary>
    private sealed class HuffmanCode
    {
        private readonly byte[] _counts;
        private readonly byte[] _symbols;
        private readonly Dictionary<int, (int Code, int Length)> _codes = [];

        private HuffmanCode(byte[] counts, byte[] symbols)
        {
            _counts = counts;
            _symbols = symbols;
            var code = 0;
            var index = 0;
            for (var length = 1; length <= 16; length++)
            {
                for (var i = 0; i < counts[length - 1]; i++)
                {
                    _codes[symbols[index++]] = (code++, length);
                }

                code <<= 1;
            }
        }

        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
        public static HuffmanCode Create(JpegTestHuffmanStyle style, int seed)
        {
            var symbols = Enumerable.Range(0, 17).Select(value => (byte)value).ToArray();
            var counts = new byte[16];
            if (style == JpegTestHuffmanStyle.Fixed)
            {
                // Seventeen codes of 5 bits
                counts[4] = 17;
                return new HuffmanCode(counts, symbols);
            }

            // One code of each length from 2 to 16 bits (two of 4 and of 16 bits) for a random permutation of the categories:
            // most of them are longer than the lookup of a decoder
            new Random(seed).Shuffle(symbols);
            for (var length = 2; length <= 16; length++)
            {
                counts[length - 1] = 1;
            }

            counts[3]++;
            counts[15]++;
            return new HuffmanCode(counts, symbols);
        }

        public byte[] ToDefinition(byte classAndId) => [classAndId, .. _counts, .. _symbols];

        public (int Bits, int Length) GetCode(int symbol) => _codes[symbol];
    }
}
