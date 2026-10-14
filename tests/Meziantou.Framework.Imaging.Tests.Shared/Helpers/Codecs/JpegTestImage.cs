using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A JPEG test image defined by its quantized DCT coefficients, with an independent test-side Huffman encoder
/// (DQT/DHT/SOF/SOS/DRI segments, DC prediction, run-length AC coding with ZRL/EOB, byte stuffing, restart markers,
/// interleaved and non-interleaved scans; progressive scans of ITU-T T.81 G.1.2 written from the specification: DC first and
/// refinement scans, AC first scans with EOB runs, AC refinement scans with correction bits; or the same scans arithmetic
/// coded, T.81 F.1.4 and G.1.3, with <see cref="JpegTestArithmeticCoder"/>) and the expected decoded pixels
/// computed from the specifications: the direct
/// two-dimensional inverse DCT of ITU-T T.81 A.3.3 evaluated in double precision, and the upsampling and color conversion
/// contract of the library transcribed pixel by pixel. Nothing here uses the library decoder. The samples have 8 bits, or
/// 12 bits (an extended sequential or progressive frame, whose expected pixels are scaled to 16 bits).
/// </summary>
/// <remarks>
/// Random blocks whose exact inverse DCT has a sample within 1e-6 of a rounding boundary are drawn again, so that the
/// expected samples never depend on the floating-point evaluation order (blocks with only a DC coefficient are exact).
/// </remarks>
internal sealed class JpegTestImage
{
    private static readonly double[][] CosineTable = CreateCosineTable();

    private JpegTestImage(int width, int height, bool rgb, JpegTestInkCoding inks, Component[] components, ushort[][] quantizationTables, int precision)
    {
        if ((components.Length == 4) != (inks != JpegTestInkCoding.None))
            throw new ArgumentException("Four components, and only them, are inks.", nameof(inks));

        if (precision is not (8 or 12))
            throw new ArgumentOutOfRangeException(nameof(precision));

        Precision = precision;
        Width = width;
        Height = height;
        Rgb = rgb;
        Inks = inks;
        Components = components;
        QuantizationTables = quantizationTables;
        MaxHorizontal = components.Max(c => c.H);
        MaxVertical = components.Max(c => c.V);
        McusX = (width + (8 * MaxHorizontal) - 1) / (8 * MaxHorizontal);
        McusY = (height + (8 * MaxVertical) - 1) / (8 * MaxVertical);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the number of bits of a sample: 8 or 12.</summary>
    public int Precision { get; }

    /// <summary>Gets the largest sample value: 255 or 4095.</summary>
    public int MaxSample => (1 << Precision) - 1;

    /// <summary>Gets a value indicating whether the samples are RGB (Adobe APP14 transform 0) rather than YCbCr.</summary>
    public bool Rgb { get; }

    /// <summary>Gets how the four components code inks, or <see cref="JpegTestInkCoding.None"/> for one or three components.</summary>
    public JpegTestInkCoding Inks { get; }

    public Component[] Components { get; }

    /// <summary>Gets the quantization tables (natural order): table 0 for the first component, table 1 for the others.</summary>
    public ushort[][] QuantizationTables { get; }

    public int MaxHorizontal { get; }

    public int MaxVertical { get; }

    public int McusX { get; }

    public int McusY { get; }

    /// <summary>Gets the pixel format of the decoded image: 12-bit samples are scaled to 16 bits, and color ones are opaque RGBA.</summary>
    public PixelFormat PixelFormat => Precision > 8
        ? Components.Length == 1 ? PixelFormat.Gray16 : PixelFormat.Rgba64
        : Components.Length == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24;

    public static ReadOnlySpan<byte> ZigZag =>
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>Creates seeded random coefficients.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="sampling">The sampling factors of each component (1, 3 or 4 components).</param>
    /// <param name="seed">The seed.</param>
    /// <param name="rgb">RGB samples instead of YCbCr (3 components).</param>
    /// <param name="acDensity">The probability of a nonzero AC coefficient (decreasing with the frequency).</param>
    /// <param name="dcOnlyProbability">The probability that a block has only a DC coefficient.</param>
    /// <param name="largeQuantization">Quantization values up to 1000 (16-bit tables).</param>
    /// <param name="inks">How the samples code inks (4 components).</param>
    /// <param name="precision">The number of bits of a sample: 8 or 12.</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public static JpegTestImage CreateRandom(int width, int height, (int H, int V)[] sampling, int seed, bool rgb = false, double acDensity = 0.35, double dcOnlyProbability = 0.2, bool largeQuantization = false, JpegTestInkCoding inks = JpegTestInkCoding.None, int precision = 8)
    {
        var random = new Random(seed);
        var tables = new ushort[2][];
        for (var t = 0; t < 2; t++)
        {
            tables[t] = new ushort[64];
            for (var i = 0; i < 64; i++)
            {
                tables[t][i] = (ushort)(largeQuantization ? random.Next(200, 1001) : random.Next(1, 41));
            }
        }

        var components = CreateComponents(width, height, sampling, rgb);
        foreach (var component in components)
        {
            var quantization = tables[component.QuantizationTable];
            for (var b = 0; b < component.Blocks.Length; b++)
            {
                int[] block;
                do
                {
                    block = CreateBlock(random, quantization, acDensity, dcOnlyProbability, precision);
                }
                while (!TryTransform(block, quantization, precision, out _));

                component.Blocks[b] = block;
            }
        }

        return new JpegTestImage(width, height, rgb, inks, components, tables, precision);
    }

    /// <summary>Creates an image from given quantized coefficients (used to build realistic progressive inputs, e.g. for benchmarks).</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="sampling">The sampling factors of each component (1 or 3 components, YCbCr; 4 components, inks).</param>
    /// <param name="quantizationTables">The two quantization tables (natural order): table 0 for the first component, table 1 for the others.</param>
    /// <param name="getBlock">Gets the 64 quantized coefficients (natural order) of a component block: component index, block column, block row.</param>
    /// <param name="inks">How the samples code inks (4 components).</param>
    /// <param name="precision">The number of bits of a sample: 8 or 12.</param>
    public static JpegTestImage FromCoefficients(int width, int height, (int H, int V)[] sampling, ushort[][] quantizationTables, Func<int, int, int, int[]> getBlock, JpegTestInkCoding inks = JpegTestInkCoding.None, int precision = 8)
    {
        var components = CreateComponents(width, height, sampling, rgb: false);
        for (var c = 0; c < components.Length; c++)
        {
            var component = components[c];
            for (var by = 0; by < component.BlocksY; by++)
            {
                for (var bx = 0; bx < component.BlocksX; bx++)
                {
                    component.Blocks[(by * component.BlocksX) + bx] = getBlock(c, bx, by);
                }
            }
        }

        return new JpegTestImage(width, height, rgb: false, inks, components, quantizationTables, precision);
    }

    /// <summary>Gets the exact inverse DCT of a block of 8-bit samples (T.81 A.3.3), level shifted, rounded (ties upward) and clamped.</summary>
    /// <returns><see langword="false"/> if a sample is within 1e-6 of a rounding boundary.</returns>
    public static bool TryTransform(int[] coefficients, ushort[] quantization, out byte[] samples)
    {
        var result = TryTransform(coefficients, quantization, precision: 8, out var values);
        samples = [.. values.Select(value => (byte)value)];
        return result;
    }

    /// <summary>Gets the exact inverse DCT of a block (T.81 A.3.3), level shifted by half the range, rounded (ties upward) and clamped.</summary>
    /// <returns><see langword="false"/> if a sample is within 1e-6 of a rounding boundary.</returns>
    public static bool TryTransform(int[] coefficients, ushort[] quantization, int precision, out int[] samples)
    {
        var center = 1 << (precision - 1);
        var max = (1 << precision) - 1;
        samples = new int[64];
        if (coefficients.AsSpan(1).IndexOfAnyExcept(0) < 0)
        {
            // Exact: DC * Q / 8, ties upward
            var value = Math.Clamp((int)Math.Floor((coefficients[0] * (double)quantization[0] / 8) + 0.5) + center, 0, max);
            samples.AsSpan().Fill(value);
            return true;
        }

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0.0;
                for (var v = 0; v < 8; v++)
                {
                    for (var u = 0; u < 8; u++)
                    {
                        var coefficient = coefficients[(v * 8) + u];
                        if (coefficient != 0)
                        {
                            sum += Scale(u) * Scale(v) * coefficient * quantization[(v * 8) + u] * CosineTable[x][u] * CosineTable[y][v];
                        }
                    }
                }

                var shifted = (sum / 4) + center;
                var rounded = Math.Floor(shifted + 0.5);
                if (Math.Abs(shifted + 0.5 - rounded) < 1e-6 && rounded > 0 && rounded <= max)
                    return false;

                samples[(y * 8) + x] = (int)Math.Clamp(rounded, 0, max);
            }
        }

        return true;

        static double Scale(int k) => k == 0 ? 1 / Math.Sqrt(2) : 1;
    }

    private static Component[] CreateComponents(int width, int height, (int H, int V)[] sampling, bool rgb)
    {
        // The only component of a grayscale frame is not subsampled, whatever its declared factors
        var maxH = sampling.Length == 1 ? 1 : sampling.Max(s => s.H);
        var maxV = sampling.Length == 1 ? 1 : sampling.Max(s => s.V);
        var mcusX = (width + (8 * maxH) - 1) / (8 * maxH);
        var mcusY = (height + (8 * maxV) - 1) / (8 * maxV);
        var components = new Component[sampling.Length];
        for (var c = 0; c < sampling.Length; c++)
        {
            var (h, v) = sampling.Length == 1 ? (1, 1) : sampling[c];
            components[c] = new Component(
                id: (byte)(rgb ? "RGB"[c] : c + 1),
                h, v,
                declared: sampling[c],
                width: (int)Math.Ceiling(width * (double)h / maxH),
                height: (int)Math.Ceiling(height * (double)v / maxV),
                blocksX: sampling.Length == 1 ? (width + 7) / 8 : mcusX * h,
                blocksY: sampling.Length == 1 ? (height + 7) / 8 : mcusY * v,
                quantizationTable: c == 0 ? 0 : 1);
        }

        return components;
    }

    /// <summary>Gets the expected 8-bit samples of a component (its real width and height).</summary>
    public byte[] GetComponentSamples(int index)
    {
        if (Precision != 8)
            throw new InvalidOperationException("The samples are not bytes: use GetComponentValues.");

        return [.. GetComponentValues(index).Select(value => (byte)value)];
    }

    /// <summary>Gets the expected samples of a component (its real width and height), at the precision of the image.</summary>
    public int[] GetComponentValues(int index)
    {
        var component = Components[index];
        var quantization = QuantizationTables[component.QuantizationTable];
        var plane = new int[component.Width * component.Height];
        for (var by = 0; by * 8 < component.Height; by++)
        {
            for (var bx = 0; bx * 8 < component.Width; bx++)
            {
                if (!TryTransform(component.Blocks[(by * component.BlocksX) + bx], quantization, Precision, out var samples))
                    throw new InvalidOperationException("Ambiguous block.");

                for (var y = 0; y < 8 && (by * 8) + y < component.Height; y++)
                {
                    for (var x = 0; x < 8 && (bx * 8) + x < component.Width; x++)
                    {
                        plane[(((by * 8) + y) * component.Width) + (bx * 8) + x] = samples[(y * 8) + x];
                    }
                }
            }
        }

        return plane;
    }

    /// <summary>
    /// Gets the expected ink amounts of a four-component image of 8-bit samples: interleaved cyan, magenta, yellow and
    /// black, where 0 is no ink. The stored samples are complements (255 is no ink). YCCK (Adobe Technical Note #5116)
    /// complements the first three stored samples again before coding them like RGB, so its color conversion gives their
    /// ink amounts directly.
    /// </summary>
    public byte[] GetExpectedInks()
    {
        if (Precision != 8)
            throw new InvalidOperationException("The amounts are not bytes: use GetExpectedDeepInks.");

        return [.. GetInkAmounts().Select(value => (byte)value)];
    }

    /// <summary>Gets the expected ink amounts of a four-component image of 12-bit samples, scaled to 16 bits like the samples of the decoded pixels.</summary>
    public ushort[] GetExpectedDeepInks()
    {
        if (Precision == 8)
            throw new InvalidOperationException("The amounts are bytes: use GetExpectedInks.");

        return [.. GetInkAmounts().Select(ScaleToSixteenBits)];
    }

    /// <summary>
    /// Gets the expected decoded pixels in <see cref="PixelFormat"/>: <see cref="PixelFormat.Gray8"/> or
    /// <see cref="PixelFormat.Rgb24"/> bytes, or the native 16-bit samples of <see cref="PixelFormat.Gray16"/> or opaque
    /// <see cref="PixelFormat.Rgba64"/> for 12-bit samples, each scaled to 16 bits.
    /// </summary>
    public byte[] GetExpectedPixels()
    {
        if (Inks != JpegTestInkCoding.None)
            throw new InvalidOperationException("The pixels of inks depend on a color conversion: use GetExpectedInks.");

        var planes = Enumerable.Range(0, Components.Length).Select(GetComponentValues).ToArray();
        var channels = Components.Length;
        var values = new int[Width * Height * channels];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * channels;
                if (channels == 1)
                {
                    values[offset] = planes[0][(y * Width) + x];
                    continue;
                }

                var c0 = Upsample(Components[0], planes[0], x, y);
                var c1 = Upsample(Components[1], planes[1], x, y);
                var c2 = Upsample(Components[2], planes[2], x, y);
                (values[offset], values[offset + 1], values[offset + 2]) = Rgb ? (c0, c1, c2) : ConvertYCbCr(c0, c1, c2);
            }
        }

        if (Precision == 8)
            return [.. values.Select(value => (byte)value)];

        // Gray16, or Rgba64 with an opaque alpha sample
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
    private ushort ScaleToSixteenBits(int sample) => (ushort)Math.Floor((sample * 65535.0 / MaxSample) + 0.5);

    private int[] GetInkAmounts()
    {
        if (Inks == JpegTestInkCoding.None)
            throw new InvalidOperationException("The image has no ink.");

        var max = MaxSample;
        var planes = Enumerable.Range(0, Components.Length).Select(GetComponentValues).ToArray();
        var inks = new int[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * 4;
                var c0 = Upsample(Components[0], planes[0], x, y);
                var c1 = Upsample(Components[1], planes[1], x, y);
                var c2 = Upsample(Components[2], planes[2], x, y);
                var c3 = Upsample(Components[3], planes[3], x, y);
                (inks[offset], inks[offset + 1], inks[offset + 2]) = Inks == JpegTestInkCoding.Ycck ? ConvertYCbCr(c0, c1, c2) : (max - c0, max - c1, max - c2);
                inks[offset + 3] = max - c3;
            }
        }

        return inks;
    }

    /// <summary>JFIF full range, 16-bit fixed-point coefficients, ties upward.</summary>
    private (int R, int G, int B) ConvertYCbCr(int luma, int blue, int red) => ConvertYCbCr(luma, blue, red, Precision);

    /// <summary>The color conversion contract of the library for samples of a precision: JFIF full range, 16-bit fixed-point coefficients, ties upward.</summary>
    internal static (int R, int G, int B) ConvertYCbCr(int luma, int blue, int red, int precision)
    {
        long max = (1 << precision) - 1;
        long cb = blue - (1 << (precision - 1));
        long cr = red - (1 << (precision - 1));
        return (Clamp(luma + (((91881 * cr) + 32768) >> 16)), Clamp(luma + (((-22554 * cb) - (46802 * cr) + 32768) >> 16)), Clamp(luma + (((116130 * cb) + 32768) >> 16)));

        int Clamp(long value) => (int)Math.Clamp(value, 0, max);
    }

    /// <summary>
    /// Gets the image whose coefficients keep only the bits a partial progression sends: the coefficient at zig-zag position
    /// <c>k</c> of component <c>c</c> keeps its bits from <c>lowBit(c, k)</c> up (DC: arithmetic shift, AC: magnitude
    /// shift, T.81 G.1.2.1), or is zero when <c>lowBit</c> is negative (never coded).
    /// </summary>
    public JpegTestImage Approximate(Func<int, int, int> lowBit)
    {
        var components = new Component[Components.Length];
        for (var c = 0; c < components.Length; c++)
        {
            var source = Components[c];
            var copy = new Component(source.Id, source.H, source.V, source.Declared, source.Width, source.Height, source.BlocksX, source.BlocksY, source.QuantizationTable);
            for (var b = 0; b < source.Blocks.Length; b++)
            {
                var block = new int[64];
                for (var k = 0; k < 64; k++)
                {
                    var low = lowBit(c, k);
                    var value = source.Blocks[b][ZigZag[k]];
                    block[ZigZag[k]] = low < 0 ? 0 : k == 0 ? (value >> low) << low : Math.Sign(value) * ((Math.Abs(value) >> low) << low);
                }

                copy.Blocks[b] = block;
            }

            components[c] = copy;
        }

        return new JpegTestImage(Width, Height, Rgb, Inks, components, QuantizationTables, Precision);
    }

    /// <summary>Encodes the image.</summary>
    public byte[] Encode(JpegTestEncodeOptions? options = null)
    {
        options ??= new JpegTestEncodeOptions();
        var output = new List<byte> { 0xFF, 0xD8 };
        if (Rgb || Inks == JpegTestInkCoding.Cmyk)
        {
            AddSegment(output, 0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 0]);
        }
        else if (Inks == JpegTestInkCoding.Ycck)
        {
            AddSegment(output, 0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 2]);
        }
        else if (Inks == JpegTestInkCoding.None)
        {
            AddSegment(output, 0xE0, [.. "JFIF\0"u8, 1, 2, 0, 0, 1, 0, 1, 0, 0]);
        }

        foreach (var segment in options.SegmentsBeforeFrame)
        {
            output.AddRange(segment);
        }

        var sixteenBit = QuantizationTables.Any(table => table.Any(value => value > 255));
        var tableCount = Components.Length == 1 ? 1 : 2;
        for (var t = 0; t < tableCount; t++)
        {
            var dqt = new List<byte> { (byte)((sixteenBit ? 0x10 : 0) | t) };
            for (var k = 0; k < 64; k++)
            {
                var value = QuantizationTables[t][ZigZag[k]];
                if (sixteenBit)
                {
                    dqt.Add((byte)(value >> 8));
                }

                dqt.Add((byte)value);
            }

            AddSegment(output, 0xDB, [.. dqt]);
        }

        var sof = new List<byte> { (byte)Precision, (byte)(Height >> 8), (byte)Height, (byte)(Width >> 8), (byte)Width, (byte)Components.Length };
        foreach (var component in Components)
        {
            sof.AddRange([component.Id, (byte)((component.Declared.H << 4) | component.Declared.V), (byte)component.QuantizationTable]);
        }

        // A baseline frame only has 8-bit samples: deeper ones are written in an extended sequential frame by default
        var progression = options.Progression;
        var frameMarker = progression is null ? options.FrameMarker ?? (Precision > 8 ? (byte)0xC1 : (byte)0xC0) : (byte)0xC2;
        if (options.Arithmetic)
        {
            frameMarker = progression is null ? (byte)0xC9 : (byte)0xCA;
        }

        AddSegment(output, frameMarker, [.. sof]);
        var tables = Enumerable.Range(0, tableCount).Select(t => (Dc: HuffmanCode.Create(false, options.Huffman, options.HuffmanSeed + (2 * t), progression is not null, Precision), Ac: HuffmanCode.Create(true, options.Huffman, options.HuffmanSeed + (2 * t) + 1, progression is not null, Precision))).ToArray();
        if (options.Arithmetic)
        {
            // No Huffman table; the conditioning that is not the default one is defined before the scans (T.81 B.2.4.3)
            var dac = new List<byte>();
            for (var t = 0; t < tableCount; t++)
            {
                if (options.DcConditioning is { } dcConditioning)
                {
                    dac.AddRange([(byte)t, (byte)((dcConditioning[t].Upper << 4) | dcConditioning[t].Lower)]);
                }

                if (options.AcConditioning is { } acConditioning)
                {
                    dac.AddRange([(byte)(0x10 | t), (byte)acConditioning[t]]);
                }
            }

            if (dac.Count > 0)
            {
                AddSegment(output, 0xCC, [.. dac]);
            }
        }
        else if (!options.TablesBeforeEachScan)
        {
            WriteHuffmanTables(output, tables);
        }

        if (options.RestartInterval > 0)
        {
            AddSegment(output, 0xDD, [(byte)(options.RestartInterval >> 8), (byte)options.RestartInterval]);
        }

        if (progression is not null)
        {
            foreach (var scan in progression)
            {
                if (options.TablesBeforeEachScan && !options.Arithmetic)
                {
                    WriteHuffmanTables(output, tables);
                }

                var sos = new List<byte> { (byte)scan.Components.Length };
                foreach (var index in scan.Components)
                {
                    var table = Components[index].QuantizationTable;
                    sos.AddRange([Components[index].Id, (byte)((table << 4) | table)]);
                }

                sos.AddRange([(byte)scan.Start, (byte)scan.End, (byte)((scan.High << 4) | scan.Low)]);
                AddSegment(output, 0xDA, [.. sos]);
                if (options.Arithmetic)
                {
                    new ArithmeticScanWriter(this, output, scan, sequential: false, options).Write(options.RestartInterval);
                }
                else
                {
                    new ProgressiveScanWriter(this, output, scan, tables, options.MaxEndOfBandRun, options.ZeroRunsBeforeEndOfBlock).Write(options.RestartInterval);
                }
            }
        }

        var scans = progression is not null ? [] : options.Scans ?? [[.. Enumerable.Range(0, Components.Length)]];
        foreach (var scan in scans)
        {
            if (options.TablesBeforeEachScan && !options.Arithmetic)
            {
                WriteHuffmanTables(output, tables);
            }

            var sos = new List<byte> { (byte)scan.Length };
            foreach (var index in scan)
            {
                var table = Components[index].QuantizationTable;
                sos.AddRange([Components[index].Id, (byte)((table << 4) | table)]);
            }

            sos.AddRange([0, 63, 0]);
            AddSegment(output, 0xDA, [.. sos]);
            if (options.Arithmetic)
            {
                new ArithmeticScanWriter(this, output, new JpegTestProgressiveScan(scan, 0, 63, 0, 0), sequential: true, options).Write(options.RestartInterval);
            }
            else
            {
                EncodeScan(output, scan, tables, options.RestartInterval, options.ZeroRunsBeforeEndOfBlock);
            }
        }

        foreach (var segment in options.SegmentsAfterScans)
        {
            output.AddRange(segment);
        }

        output.AddRange([0xFF, 0xD9]);
        return [.. output];
    }

    /// <summary>Packs codes most significant bit first into entropy-coded bytes (stuffed, padded with 1-bits).</summary>
    public static byte[] PackBits(IEnumerable<(int Bits, int Length)> codes)
    {
        var output = new List<byte>();
        var writer = new BitWriter(output);
        foreach (var (bits, length) in codes)
        {
            writer.Write(bits, length);
        }

        writer.Flush();
        return [.. output];
    }

    public static byte[] Segment(byte marker, ReadOnlySpan<byte> payload)
    {
        var result = new byte[payload.Length + 4];
        result[0] = 0xFF;
        result[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(result.AsSpan(4));
        return result;
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static int[] CreateBlock(Random random, ushort[] quantization, double acDensity, double dcOnlyProbability, int precision)
    {
        var block = new int[64];

        // Mean sample offset within the range of the samples (occasionally beyond, to exercise clamping)
        var scale = 1 << (precision - 8);
        var mean = random.Next(-140 * scale, (140 * scale) + 1);
        block[0] = (int)Math.Round(mean * 8.0 / quantization[0], MidpointRounding.AwayFromZero);
        if (precision > 8)
        {
            // The difference of two DC coefficients must stay within the category 15
            block[0] = Math.Clamp(block[0], -16383, 16383);
        }

        if (random.NextDouble() < dcOnlyProbability)
            return block;

        for (var k = 1; k < 64; k++)
        {
            if (random.NextDouble() < acDensity * (1 - (k / 80.0)))
            {
                var natural = ZigZag[k];
                var limit = Math.Clamp(400 * scale / quantization[natural], 1, precision > 8 ? 16383 : 1023);
                var value = random.Next(1, limit + 1);
                block[natural] = random.Next(2) == 0 ? value : -value;
            }
        }

        return block;
    }

    private static double[][] CreateCosineTable()
    {
        var table = new double[8][];
        for (var x = 0; x < 8; x++)
        {
            table[x] = new double[8];
            for (var u = 0; u < 8; u++)
            {
                table[x][u] = Math.Cos((((2 * x) + 1) * u * Math.PI) / 16);
            }
        }

        return table;
    }

    private static void AddSegment(List<byte> output, byte marker, byte[] payload) => output.AddRange(Segment(marker, payload));

    private static void WriteHuffmanTables(List<byte> output, (HuffmanCode Dc, HuffmanCode Ac)[] tables)
    {
        for (var t = 0; t < tables.Length; t++)
        {
            AddSegment(output, 0xC4, tables[t].Dc.ToDefinition((byte)t));
            AddSegment(output, 0xC4, tables[t].Ac.ToDefinition((byte)(0x10 | t)));
        }
    }

    private static int GetSize(int value)
    {
        var magnitude = Math.Abs(value);
        var size = 0;
        while (magnitude > 0)
        {
            size++;
            magnitude >>= 1;
        }

        return size;
    }

    /// <summary>The upsampling contract of the library, one output sample at a time.</summary>
    private int Upsample(Component component, int[] plane, int x, int y)
        => Upsample(plane, component.Width, component.Height, MaxHorizontal / component.H, MaxVertical / component.V, x, y);

    /// <summary>The upsampling contract of the library, one output sample at a time.</summary>
    /// <param name="plane">The samples of the component.</param>
    /// <param name="width">The number of samples of a row of the component.</param>
    /// <param name="height">The number of rows of the component.</param>
    /// <param name="rh">The horizontal upsampling factor.</param>
    /// <param name="rv">The vertical upsampling factor.</param>
    /// <param name="x">The column of the output sample.</param>
    /// <param name="y">The row of the output sample.</param>
    internal static int Upsample(int[] plane, int width, int height, int rh, int rv, int x, int y)
    {
        int S(int sx, int sy) => plane[(Math.Clamp(sy, 0, height - 1) * width) + Math.Clamp(sx, 0, width - 1)];
        if (rh == 1 && rv == 1)
            return S(x, y);

        // A horizontal factor of 2 on a plane of at most 2 samples replicates in both directions (libjpeg-turbo's rule)
        if (rh == 2 && width <= 2)
            return S(x / rh, y / rv);

        if (rv == 2 && rh <= 2)
        {
            var row = y / 2;
            var far = y % 2 == 0 ? row - 1 : row + 1;
            if (rh == 1)
                return ((3 * S(x, row)) + S(x, far) + (y % 2 == 0 ? 1 : 2)) >> 2;

            var column = x / 2;
            var neighbor = x % 2 == 0 ? column - 1 : column + 1;
            var near = (3 * S(column, row)) + S(column, far);
            var other = (3 * S(neighbor, row)) + S(neighbor, far);
            return ((3 * near) + other + (x % 2 == 0 ? 8 : 7)) >> 4;
        }

        if (rv == 1 && rh == 2)
        {
            var column = x / 2;
            var neighbor = x % 2 == 0 ? column - 1 : column + 1;
            return ((3 * S(column, y)) + S(neighbor, y) + (x % 2 == 0 ? 1 : 2)) >> 2;
        }

        return S(x / rh, y / rv);
    }

    private void EncodeScan(List<byte> output, int[] scan, (HuffmanCode Dc, HuffmanCode Ac)[] tables, int restartInterval, int zeroRunsBeforeEndOfBlock)
    {
        var writer = new BitWriter(output);
        var predictions = new int[Components.Length];
        var interleaved = scan.Length > 1;
        var single = Components[scan[0]];
        var mcusX = interleaved ? McusX : (single.Width + 7) / 8;
        var mcusY = interleaved ? McusY : (single.Height + 7) / 8;
        var total = mcusX * mcusY;
        var restartIndex = 0;
        for (var mcu = 0; mcu < total; mcu++)
        {
            if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
            {
                writer.Flush();
                output.AddRange([0xFF, (byte)(0xD0 + (restartIndex & 7))]);
                restartIndex++;
                Array.Clear(predictions);
            }

            var mx = mcu % mcusX;
            var my = mcu / mcusX;
            foreach (var index in scan)
            {
                var component = Components[index];
                var (dc, ac) = tables[component.QuantizationTable];
                var h = interleaved ? component.H : 1;
                var v = interleaved ? component.V : 1;
                for (var by = 0; by < v; by++)
                {
                    for (var bx = 0; bx < h; bx++)
                    {
                        var block = component.Blocks[((((my * v) + by) * component.BlocksX) + (mx * h)) + bx];
                        EncodeBlock(writer, block, ref predictions[index], dc, ac, zeroRunsBeforeEndOfBlock);
                    }
                }
            }
        }

        writer.Flush();
    }

    private static void EncodeBlock(BitWriter writer, int[] block, ref int prediction, HuffmanCode dc, HuffmanCode ac, int zeroRunsBeforeEndOfBlock)
    {
        var difference = block[0] - prediction;
        prediction = block[0];
        var size = GetSize(difference);
        dc.Write(writer, size);
        WriteValue(writer, difference, size);
        var run = 0;
        for (var k = 1; k < 64; k++)
        {
            var value = block[ZigZag[k]];
            if (value == 0)
            {
                run++;
                continue;
            }

            while (run > 15)
            {
                ac.Write(writer, 0xF0);
                run -= 16;
            }

            size = GetSize(value);
            ac.Write(writer, (run << 4) | size);
            WriteValue(writer, value, size);
            run = 0;
        }

        if (run > 0)
        {
            // Runs of zeros that the end of block makes useless, when zeros are left for it
            for (var i = 0; i < zeroRunsBeforeEndOfBlock && run > 16; i++)
            {
                ac.Write(writer, 0xF0);
                run -= 16;
            }

            ac.Write(writer, 0x00);
        }

        static void WriteValue(BitWriter writer, int value, int size)
        {
            if (size > 0)
            {
                writer.Write(value >= 0 ? value : value + (1 << size) - 1, size);
            }
        }
    }

    /// <summary>A frame component and its quantized coefficients (natural order), over the padded block grid of the frame.</summary>
    internal sealed class Component
    {
        public Component(byte id, int h, int v, (int H, int V) declared, int width, int height, int blocksX, int blocksY, int quantizationTable)
        {
            Id = id;
            H = h;
            V = v;
            Declared = declared;
            Width = width;
            Height = height;
            BlocksX = blocksX;
            BlocksY = blocksY;
            QuantizationTable = quantizationTable;
            Blocks = new int[blocksX * blocksY][];
        }

        public byte Id { get; }

        public int H { get; }

        public int V { get; }

        /// <summary>Gets the sampling factors written in the frame header (those of a grayscale frame play no role).</summary>
        public (int H, int V) Declared { get; }

        public int Width { get; }

        public int Height { get; }

        public int BlocksX { get; }

        public int BlocksY { get; }

        public int QuantizationTable { get; }

        public int[][] Blocks { get; }
    }

    /// <summary>A canonical Huffman code (T.81 C) built from per-length counts, for encoding.</summary>
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
        public static HuffmanCode Create(bool ac, JpegTestHuffmanStyle style, int seed, bool progressive = false, int precision = 8)
        {
            // Progressive AC tables also code the end-of-band runs EOB1-EOB14 (T.81 G.1.2.2). The samples of 12 bits have
            // four more categories: DC differences up to 15 and AC coefficients up to 14 (T.81 tables F.6 and F.7)
            var acCategories = precision > 8 ? 14 : 10;
            var symbols = ac
                ? [0x00, 0xF0, .. Enumerable.Range(0, 16).SelectMany(run => Enumerable.Range(1, acCategories).Select(size => (byte)((run << 4) | size))), .. progressive ? Enumerable.Range(1, 14).Select(run => (byte)(run << 4)) : []]
                : Enumerable.Range(0, precision > 8 ? 16 : 12).Select(value => (byte)value).ToArray();
            var counts = new byte[16];
            if (style == JpegTestHuffmanStyle.Fixed)
            {
                // The sixteen DC categories of 12-bit samples need 5-bit codes: a code of 1-bits only is not allowed
                counts[ac ? 7 : precision > 8 ? 4 : 3] = (byte)symbols.Length;
                return new HuffmanCode(counts, symbols);
            }

            var random = new Random(seed);
            random.Shuffle(symbols);

            // Lengths 2..16: a few short codes, then more and more long ones; one slot of length 16 stays unused (no all-ones code)
            var remaining = symbols.Length;
            long free = 1; // unused codes at the current length
            for (var length = 1; length <= 16 && remaining > 0; length++)
            {
                free *= 2;
                var capacityAfter = (long)1 << (16 - length); // codes of length 16 per unused code of this length
                var desired = length == 16 ? remaining : length < 2 ? 0 : Math.Max(1, (length - 1) * (ac ? 2 : 1) / 3);
                var count = (int)Math.Min(desired, remaining);
                while (count > 0 && ((free - count) * capacityAfter) - 1 < remaining - count)
                {
                    count--;
                }

                counts[length - 1] = (byte)count;
                free -= count;
                remaining -= count;
            }

            if (remaining != 0)
                throw new InvalidOperationException("The skewed Huffman code does not fit in 16 bits.");

            return new HuffmanCode(counts, symbols);
        }

        public byte[] ToDefinition(byte classAndId) => [classAndId, .. _counts, .. _symbols];

        public void Write(BitWriter writer, int symbol)
        {
            var (code, length) = _codes[symbol];
            writer.Write(code, length);
        }
    }

    /// <summary>
    /// Writes the entropy-coded data of one progressive scan, written from ITU-T T.81 G.1.2: DC first scans (point-transformed
    /// differences), DC refinements (one bit), AC first scans (run-lengths, EOB runs across blocks) and AC refinements
    /// (newly nonzero coefficients with their sign, correction bits of the others after the symbol that passes them or after
    /// the EOB run that ends their block).
    /// </summary>
    private sealed class ProgressiveScanWriter
    {
        private readonly JpegTestImage _image;
        private readonly List<byte> _output;
        private readonly JpegTestProgressiveScan _scan;
        private readonly (HuffmanCode Dc, HuffmanCode Ac)[] _tables;
        private readonly int _maxEndOfBandRun;
        private readonly int _zeroRunsBeforeEndOfBlock;
        private readonly List<int> _endOfBandBits = [];
        private readonly int[] _predictions;
        private readonly BitWriter _writer;
        private int _endOfBandRun;

        public ProgressiveScanWriter(JpegTestImage image, List<byte> output, JpegTestProgressiveScan scan, (HuffmanCode Dc, HuffmanCode Ac)[] tables, int maxEndOfBandRun, int zeroRunsBeforeEndOfBlock)
        {
            _image = image;
            _output = output;
            _scan = scan;
            _tables = tables;
            _maxEndOfBandRun = maxEndOfBandRun;
            _zeroRunsBeforeEndOfBlock = zeroRunsBeforeEndOfBlock;
            _predictions = new int[image.Components.Length];
            _writer = new BitWriter(output);
        }

        private HuffmanCode Ac => _tables[_image.Components[_scan.Components[0]].QuantizationTable].Ac;

        public void Write(int restartInterval)
        {
            var interleaved = _scan.Components.Length > 1;
            var single = _image.Components[_scan.Components[0]];
            var mcusX = interleaved ? _image.McusX : (single.Width + 7) / 8;
            var mcusY = interleaved ? _image.McusY : (single.Height + 7) / 8;
            var total = mcusX * mcusY;
            var restartIndex = 0;
            for (var mcu = 0; mcu < total; mcu++)
            {
                if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
                {
                    FlushEndOfBandRun();
                    _writer.Flush();
                    _output.AddRange([0xFF, (byte)(0xD0 + (restartIndex & 7))]);
                    restartIndex++;
                    Array.Clear(_predictions);
                }

                var mx = mcu % mcusX;
                var my = mcu / mcusX;
                foreach (var index in _scan.Components)
                {
                    var component = _image.Components[index];
                    var h = interleaved ? component.H : 1;
                    var v = interleaved ? component.V : 1;
                    for (var by = 0; by < v; by++)
                    {
                        for (var bx = 0; bx < h; bx++)
                        {
                            var block = component.Blocks[((((my * v) + by) * component.BlocksX) + (mx * h)) + bx];
                            WriteBlock(index, block);
                        }
                    }
                }
            }

            FlushEndOfBandRun();
            _writer.Flush();
        }

        private void WriteBlock(int componentIndex, int[] block)
        {
            var low = _scan.Low;
            if (_scan.Start == 0)
            {
                if (_scan.High != 0)
                {
                    _writer.Write((block[0] >> low) & 1, 1);
                    return;
                }

                // Arithmetic shift of the DC value (T.81 G.1.2.1), then the sequential DC coding of the difference
                var value = block[0] >> low;
                var difference = value - _predictions[componentIndex];
                _predictions[componentIndex] = value;
                var size = GetSize(difference);
                _tables[_image.Components[componentIndex].QuantizationTable].Dc.Write(_writer, size);
                WriteValue(difference, size);
                return;
            }

            if (_scan.High == 0)
            {
                WriteAcFirst(block, low);
            }
            else
            {
                WriteAcRefinement(block, low);
            }
        }

        private void WriteAcFirst(int[] block, int low)
        {
            var run = 0;
            for (var k = _scan.Start; k <= Math.Min(_scan.End, 63); k++)
            {
                var coefficient = block[ZigZag[k]];
                var value = Math.Sign(coefficient) * (Math.Abs(coefficient) >> low);
                if (value == 0)
                {
                    run++;
                    continue;
                }

                FlushEndOfBandRun();
                while (run > 15)
                {
                    Ac.Write(_writer, 0xF0);
                    run -= 16;
                }

                var size = GetSize(value);
                Ac.Write(_writer, (run << 4) | size);
                WriteValue(value, size);
                run = 0;
            }

            if (run > 0)
            {
                // Runs of zeros that the end of band makes useless, when zeros are left for it: they belong to this block,
                // so the run of the blocks before it ends first
                for (var i = 0; i < _zeroRunsBeforeEndOfBlock && run > 16; i++)
                {
                    FlushEndOfBandRun();
                    Ac.Write(_writer, 0xF0);
                    run -= 16;
                }

                _endOfBandRun++;
                if (_endOfBandRun == _maxEndOfBandRun)
                {
                    FlushEndOfBandRun();
                }
            }
        }

        private void WriteAcRefinement(int[] block, int low)
        {
            var magnitudes = new int[64];
            var lastNew = -1;
            for (var k = _scan.Start; k <= Math.Min(_scan.End, 63); k++)
            {
                magnitudes[k] = Math.Abs(block[ZigZag[k]]) >> low;
                if (magnitudes[k] == 1)
                {
                    lastNew = k;
                }
            }

            var run = 0;
            var corrections = new List<int>();
            for (var k = _scan.Start; k <= Math.Min(_scan.End, 63); k++)
            {
                var magnitude = magnitudes[k];
                if (magnitude == 0)
                {
                    run++;
                    continue;
                }

                // ZRLs only when a newly nonzero coefficient follows (otherwise the zeros end in the EOB run)
                while (run > 15 && k <= lastNew)
                {
                    FlushEndOfBandRun();
                    Ac.Write(_writer, 0xF0);
                    run -= 16;
                    WriteBits(corrections);
                }

                if (magnitude > 1)
                {
                    corrections.Add(magnitude & 1); // already nonzero: its correction bit follows the next symbol
                    continue;
                }

                FlushEndOfBandRun();
                Ac.Write(_writer, (run << 4) | 1);
                _writer.Write(block[ZigZag[k]] > 0 ? 1 : 0, 1);
                WriteBits(corrections);
                run = 0;
            }

            if (run > 0 || corrections.Count > 0)
            {
                _endOfBandRun++;
                _endOfBandBits.AddRange(corrections);
                if (_endOfBandRun == _maxEndOfBandRun || _endOfBandBits.Count > 900)
                {
                    FlushEndOfBandRun();
                }
            }
        }

        /// <summary>Writes the pending EOBn symbol (n = floor(log2(run)), then the n low bits of the run) and its correction bits.</summary>
        private void FlushEndOfBandRun()
        {
            if (_endOfBandRun == 0)
                return;

            var bits = 0;
            while ((_endOfBandRun >> (bits + 1)) != 0)
            {
                bits++;
            }

            Ac.Write(_writer, bits << 4);
            if (bits > 0)
            {
                _writer.Write(_endOfBandRun - (1 << bits), bits);
            }

            WriteBits(_endOfBandBits);
            _endOfBandRun = 0;
        }

        private void WriteBits(List<int> bits)
        {
            foreach (var bit in bits)
            {
                _writer.Write(bit, 1);
            }

            bits.Clear();
        }

        private void WriteValue(int value, int size)
        {
            if (size > 0)
            {
                _writer.Write(value >= 0 ? value : value + (1 << size) - 1, size);
            }
        }
    }

    /// <summary>
    /// Writes the entropy-coded data of one arithmetic-coded scan, written from ITU-T T.81: a sequential scan (F.1.4: the
    /// DC difference then the AC coefficients of each block) or a progressive one (G.1.3: DC first scans, DC refinements,
    /// AC first scans and AC refinements). The statistics, the DC classes and the predictions start again at each restart
    /// marker.
    /// </summary>
    private sealed class ArithmeticScanWriter
    {
        // Table F.4: the 15 magnitude category bins of a DC table; table F.5: the 14 of each half of an AC table. The bin
        // of the bits of a category is 14 bins further
        private const int DcCategoryBins = 20;
        private const int AcLowCategoryBins = 189;
        private const int AcHighCategoryBins = 217;

        private readonly JpegTestImage _image;
        private readonly List<byte> _output;
        private readonly JpegTestProgressiveScan _scan;
        private readonly bool _sequential;
        private readonly JpegTestEncodeOptions _options;
        private readonly int[] _predictions;
        private readonly int[] _dcContexts;
        private (int State, int Mps)[][] _dcStatistics = [];
        private (int State, int Mps)[][] _acStatistics = [];
        private JpegTestArithmeticCoder _coder;

        public ArithmeticScanWriter(JpegTestImage image, List<byte> output, JpegTestProgressiveScan scan, bool sequential, JpegTestEncodeOptions options)
        {
            _image = image;
            _output = output;
            _scan = scan;
            _sequential = sequential;
            _options = options;
            _predictions = new int[image.Components.Length];
            _dcContexts = new int[image.Components.Length];
            _coder = Start();
        }

        public void Write(int restartInterval)
        {
            var interleaved = _scan.Components.Length > 1;
            var single = _image.Components[_scan.Components[0]];
            var mcusX = interleaved ? _image.McusX : (single.Width + 7) / 8;
            var mcusY = interleaved ? _image.McusY : (single.Height + 7) / 8;
            var total = mcusX * mcusY;
            var restartIndex = 0;
            for (var mcu = 0; mcu < total; mcu++)
            {
                if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
                {
                    _coder.Flush(_options.DiscardFinalZeros);
                    _output.AddRange([0xFF, (byte)(0xD0 + (restartIndex & 7))]);
                    restartIndex++;
                    Array.Clear(_predictions);
                    Array.Clear(_dcContexts);
                    _coder = Start();
                }

                var mx = mcu % mcusX;
                var my = mcu / mcusX;
                foreach (var index in _scan.Components)
                {
                    var component = _image.Components[index];
                    var h = interleaved ? component.H : 1;
                    var v = interleaved ? component.V : 1;
                    for (var by = 0; by < v; by++)
                    {
                        for (var bx = 0; bx < h; bx++)
                        {
                            WriteBlock(index, component.Blocks[((((my * v) + by) * component.BlocksX) + (mx * h)) + bx]);
                        }
                    }
                }
            }

            _coder.Flush(_options.DiscardFinalZeros);
        }

        /// <summary>A new entropy-coded segment: every statistics bin is the state 0 with the more probable symbol 0.</summary>
        private JpegTestArithmeticCoder Start()
        {
            _dcStatistics = [new (int, int)[49], new (int, int)[49]];
            _acStatistics = [new (int, int)[245], new (int, int)[245]];
            return new JpegTestArithmeticCoder(_output);
        }

        private void WriteBlock(int componentIndex, int[] block)
        {
            var table = _image.Components[componentIndex].QuantizationTable;
            var low = _scan.Low;
            if (_sequential)
            {
                WriteDcDifference(componentIndex, table, block[0]);
                WriteAcCoefficients(table, block, 1, 63, 0);
            }
            else if (_scan.Start == 0)
            {
                if (_scan.High != 0)
                {
                    // One more bit of the DC value, with the fixed estimate (G.1.3.1)
                    _coder.CodeFixed((block[0] >> low) & 1);
                }
                else
                {
                    WriteDcDifference(componentIndex, table, block[0] >> low);
                }
            }
            else if (_scan.High == 0)
            {
                WriteAcCoefficients(table, block, _scan.Start, Math.Min(_scan.End, 63), low);
            }
            else
            {
                WriteAcRefinement(table, block, _scan.Start, Math.Min(_scan.End, 63), low);
            }
        }

        /// <summary>
        /// The difference of a DC value with the one of the previous block of the component (figure F.4): zero or not, the
        /// sign, then the magnitude. The first bins are those of the class of the previous difference (zero, small or
        /// large, positive or negative, with the bounds L and U of the table).
        /// </summary>
        private void WriteDcDifference(int componentIndex, int table, int value)
        {
            var statistics = _dcStatistics[table];
            var context = _dcContexts[componentIndex];
            var difference = value - _predictions[componentIndex];
            _predictions[componentIndex] = value;
            if (difference == 0)
            {
                _coder.Code(ref statistics[context], 0);
                _dcContexts[componentIndex] = 0;
                return;
            }

            _coder.Code(ref statistics[context], 1);
            var negative = difference < 0;
            _coder.Code(ref statistics[context + 1], negative ? 1 : 0);
            var size = Math.Abs(difference) - 1;
            ref var first = ref statistics[context + (negative ? 3 : 2)];
            if (size == 0)
            {
                _coder.Code(ref first, 0);
            }
            else
            {
                _coder.Code(ref first, 1);
                WriteMagnitude(statistics, DcCategoryBins, size, 2);
            }

            var (lower, upper) = _options.DcConditioning?[table] ?? (0, 1);
            var magnitude = Math.Abs(difference);
            var zero = lower == 0 ? 0 : 1 << (lower - 1);
            _dcContexts[componentIndex] = magnitude <= zero ? 0 : (magnitude <= 1 << upper ? 4 : 12) + (negative ? 4 : 0);
        }

        /// <summary>
        /// The AC coefficients of a band, each divided by <c>2^low</c> (figures F.5 and following): for the coefficient
        /// <c>k</c>, the bin <c>3(k - 1)</c> tells whether the block ends here (only asked at the start of the band and
        /// after a nonzero coefficient), the next one whether the coefficient is zero, the third one whether its magnitude
        /// is above 1, then above 2; the sign has the fixed estimate.
        /// </summary>
        private void WriteAcCoefficients(int table, int[] block, int start, int end, int low)
        {
            var statistics = _acStatistics[table];
            var threshold = _options.AcConditioning?[table] ?? 5;
            var values = new int[64];
            var last = start - 1;
            for (var k = start; k <= end; k++)
            {
                var coefficient = block[ZigZag[k]];
                values[k] = Math.Sign(coefficient) * (Math.Abs(coefficient) >> low);
                if (values[k] != 0)
                {
                    last = k;
                }
            }

            for (var k = start; k <= end; k++)
            {
                var bin = 3 * (k - 1);
                if (k > last)
                {
                    _coder.Code(ref statistics[bin], 1); // end of block
                    return;
                }

                _coder.Code(ref statistics[bin], 0);
                while (values[k] == 0)
                {
                    _coder.Code(ref statistics[bin + 1], 0);
                    bin += 3;
                    k++;
                }

                _coder.Code(ref statistics[bin + 1], 1);
                _coder.CodeFixed(values[k] < 0 ? 1 : 0);
                var size = Math.Abs(values[k]) - 1;
                if (size == 0)
                {
                    _coder.Code(ref statistics[bin + 2], 0);
                }
                else
                {
                    _coder.Code(ref statistics[bin + 2], 1);
                    if (size == 1)
                    {
                        _coder.Code(ref statistics[bin + 2], 0);
                    }
                    else
                    {
                        _coder.Code(ref statistics[bin + 2], 1);
                        WriteMagnitude(statistics, k <= threshold ? AcLowCategoryBins : AcHighCategoryBins, size, 4);
                    }
                }
            }
        }

        /// <summary>
        /// The category of a magnitude minus one (is it at least <paramref name="bound"/>, twice that, and so on: one bin
        /// each), then its bits below the leading one, in the bin 14 bins after the one that ended the category.
        /// </summary>
        private void WriteMagnitude((int State, int Mps)[] statistics, int bin, int size, int bound)
        {
            while (size >= bound)
            {
                _coder.Code(ref statistics[bin], 1);
                bound <<= 1;
                bin++;
            }

            _coder.Code(ref statistics[bin], 0);
            bin += 14;
            for (var bit = bound >> 2; bit != 0; bit >>= 1)
            {
                _coder.Code(ref statistics[bin], (size & bit) != 0 ? 1 : 0);
            }
        }

        /// <summary>
        /// One more bit of the AC coefficients of a band (G.1.3.3). The end of the block is only asked past the last
        /// coefficient that the previous scans made nonzero; a coefficient that is already nonzero codes its bit in the
        /// third bin, one that is still zero whether it becomes nonzero in the second bin, then its sign.
        /// </summary>
        private void WriteAcRefinement(int table, int[] block, int start, int end, int low)
        {
            var statistics = _acStatistics[table];
            var last = start - 1;
            var previousLast = start - 1;
            for (var k = start; k <= end; k++)
            {
                var magnitude = Math.Abs(block[ZigZag[k]]) >> low;
                if (magnitude != 0)
                {
                    last = k;
                }

                if (magnitude > 1)
                {
                    previousLast = k;
                }
            }

            for (var k = start; k <= end; k++)
            {
                var bin = 3 * (k - 1);
                if (k > previousLast)
                {
                    if (k > last)
                    {
                        _coder.Code(ref statistics[bin], 1); // end of block
                        return;
                    }

                    _coder.Code(ref statistics[bin], 0);
                }

                while (true)
                {
                    var coefficient = block[ZigZag[k]];
                    var magnitude = Math.Abs(coefficient) >> low;
                    if (magnitude > 1)
                    {
                        _coder.Code(ref statistics[bin + 2], magnitude & 1);
                        break;
                    }

                    if (magnitude == 1)
                    {
                        _coder.Code(ref statistics[bin + 1], 1);
                        _coder.CodeFixed(coefficient < 0 ? 1 : 0);
                        break;
                    }

                    _coder.Code(ref statistics[bin + 1], 0);
                    bin += 3;
                    k++;
                }
            }
        }
    }

    /// <summary>Writes bits most significant first, stuffing a zero byte after every 0xFF and padding with 1-bits.</summary>
    private sealed class BitWriter(List<byte> output)
    {
        private int _accumulator;
        private int _count;

        public void Write(int bits, int length)
        {
            for (var i = length - 1; i >= 0; i--)
            {
                _accumulator = (_accumulator << 1) | ((bits >> i) & 1);
                _count++;
                if (_count == 8)
                {
                    Emit();
                }
            }
        }

        public void Flush()
        {
            while (_count != 0)
            {
                Write(1, 1);
            }
        }

        private void Emit()
        {
            var value = (byte)_accumulator;
            output.Add(value);
            if (value == 0xFF)
            {
                output.Add(0x00);
            }

            _accumulator = 0;
            _count = 0;
        }
    }
}
