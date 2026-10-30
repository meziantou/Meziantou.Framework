using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG encoder registration: 8-bit Huffman JPEG, baseline sequential (SOF0) or progressive (SOF2), grayscale or
/// YCbCr, written by one streaming session.
/// </summary>
internal sealed class JpegEncoderCodec : ImageEncoderCodec
{
    /// <summary>The worst-case size of one entropy-coded block: DC (at most 11 + 11 bits) and 63 AC coefficients (at most 16 + 10 bits each), doubled for byte stuffing.</summary>
    internal const int MaxBlockBytes = 2 * ((22 + (63 * 26) + 7) / 8);

    public static JpegEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Jpeg;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>Gets the luma sampling factors of an output (chroma components are always 1x1), or (1, 1) for grayscale output.</summary>
    /// <param name="pixelFormat">The pixel format of the frames.</param>
    /// <param name="subsampling">The encoder setting.</param>
    /// <returns>Whether the output has a single (grayscale) component, and the horizontal and vertical luma factors.</returns>
    internal static (bool Grayscale, int H, int V) GetSampling(PixelFormat pixelFormat, JpegChromaSubsampling subsampling)
    {
        // The component layout follows the pixel format, never the data: gray formats are one component (no chroma to
        // subsample, whatever the setting); color formats are YCbCr, Auto meaning 4:2:0
        if (PixelFormats.IsGrayscale(pixelFormat))
            return (true, 1, 1);

        return subsampling switch
        {
            JpegChromaSubsampling.Ratio444 => (false, 1, 1),
            JpegChromaSubsampling.Ratio422 => (false, 2, 1),
            _ => (false, 2, 2),
        };
    }

    /// <summary>
    /// Converts 8-bit RGB pixels to level-shifted JFIF 1.02 full-range YCbCr samples (ITU-R BT.601 luma weights):
    /// <c>Y = 0.299 R + 0.587 G + 0.114 B</c>, <c>Cb = (B - Y) / 1.772</c>, <c>Cr = (R - Y) / 1.402</c>, computed in double
    /// precision and stored unrounded (single precision) after the level shift of 128 (<c>Y - 128</c>), so the only rounding
    /// before the DCT is the conversion to single precision.
    /// </summary>
    /// <param name="source">The interleaved RGB samples, 3 bytes per pixel.</param>
    /// <param name="luma">The level-shifted luma samples; its length is the number of pixels.</param>
    /// <param name="blue">The Cb samples minus 128.</param>
    /// <param name="red">The Cr samples minus 128.</param>
    /// <remarks>
    /// With 128-bit hardware vectors, 16 pixels are deinterleaved by byte shuffles and converted two at a time with the same
    /// operations in the same order (separate multiplications and additions, the divisions kept), so the samples are identical;
    /// with AVX2, four at a time.
    /// </remarks>
    internal static void ConvertRgbToYCbCr(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
    {
        var done = Avx2.IsSupported ? ConvertRgbToYCbCrAvx2(source, luma, blue, red)
            : Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian ? ConvertRgbToYCbCrVector(source, luma, blue, red)
            : 0;
        ConvertRgbToYCbCrScalar(source[(3 * done)..], luma[done..], blue[done..], red[done..]);
    }

    /// <summary>The scalar reference of <see cref="ConvertRgbToYCbCr"/>.</summary>
    internal static void ConvertRgbToYCbCrScalar(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
    {
        for (var x = 0; x < luma.Length; x++)
        {
            double r = source[3 * x];
            double g = source[(3 * x) + 1];
            double b = source[(3 * x) + 2];
            var y = (0.299 * r) + (0.587 * g) + (0.114 * b);
            luma[x] = (float)(y - 128);
            blue[x] = (float)((b - y) / 1.772);
            red[x] = (float)((r - y) / 1.402);
        }
    }

    /// <summary>Converts the pixels of whole 16-pixel groups.</summary>
    /// <returns>The number of pixels converted.</returns>
    internal static int ConvertRgbToYCbCrVector(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
    {
        var count = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), source.Length / 3);
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        var x = 0;
        for (; x + 16 <= count; x += 16)
        {
            var (r, g, b) = Deinterleave(ref unsafe(Unsafe.Add(ref sourceStart, 3 * x)));
            var (r0, r1) = Vector128.Widen(r);
            var (g0, g1) = Vector128.Widen(g);
            var (b0, b1) = Vector128.Widen(b);
            Convert8(r0, g0, b0, ref unsafe(Unsafe.Add(ref lumaStart, x)), ref unsafe(Unsafe.Add(ref blueStart, x)), ref unsafe(Unsafe.Add(ref redStart, x)));
            Convert8(r1, g1, b1, ref unsafe(Unsafe.Add(ref lumaStart, x + 8)), ref unsafe(Unsafe.Add(ref blueStart, x + 8)), ref unsafe(Unsafe.Add(ref redStart, x + 8)));
        }

        return x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Convert8(Vector128<ushort> r, Vector128<ushort> g, Vector128<ushort> b, ref float luma, ref float blue, ref float red)
        {
            var (r0, r1) = Vector128.Widen(r);
            var (g0, g1) = Vector128.Widen(g);
            var (b0, b1) = Vector128.Widen(b);
            Convert4(r0, g0, b0, ref luma, ref blue, ref red);
            Convert4(r1, g1, b1, ref unsafe(Unsafe.Add(ref luma, 4)), ref unsafe(Unsafe.Add(ref blue, 4)), ref unsafe(Unsafe.Add(ref red, 4)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Convert4(Vector128<uint> r, Vector128<uint> g, Vector128<uint> b, ref float luma, ref float blue, ref float red)
        {
            if (Sse2.IsSupported)
            {
                // x64 has no 64-bit integer conversion before AVX-512: CVTDQ2PD converts the two lower 32-bit integers
                Convert2(ToDouble(r), ToDouble(g), ToDouble(b), out var lowLuma, out var lowBlue, out var lowRed);
                Convert2(ToDouble(Upper(r)), ToDouble(Upper(g)), ToDouble(Upper(b)), out var highLuma, out var highBlue, out var highRed);
                unsafe { Vector128.Narrow(lowLuma, highLuma).StoreUnsafe(ref luma); }
                unsafe { Vector128.Narrow(lowBlue, highBlue).StoreUnsafe(ref blue); }
                unsafe { Vector128.Narrow(lowRed, highRed).StoreUnsafe(ref red); }
                return;
            }

            var (r0, r1) = Vector128.Widen(r);
            var (g0, g1) = Vector128.Widen(g);
            var (b0, b1) = Vector128.Widen(b);
            Convert2(Vector128.ConvertToDouble(r0), Vector128.ConvertToDouble(g0), Vector128.ConvertToDouble(b0), out var y0, out var cb0, out var cr0);
            Convert2(Vector128.ConvertToDouble(r1), Vector128.ConvertToDouble(g1), Vector128.ConvertToDouble(b1), out var y1, out var cb1, out var cr1);
            unsafe { Vector128.Narrow(y0, y1).StoreUnsafe(ref luma); }
            unsafe { Vector128.Narrow(cb0, cb1).StoreUnsafe(ref blue); }
            unsafe { Vector128.Narrow(cr0, cr1).StoreUnsafe(ref red); }
        }

        static Vector128<double> ToDouble(Vector128<uint> value) => Sse2.ConvertToVector128Double(value.AsInt32());

        static Vector128<uint> Upper(Vector128<uint> value) => Sse2.UnpackHigh(value.AsUInt64(), value.AsUInt64()).AsUInt32();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Convert2(Vector128<double> r, Vector128<double> g, Vector128<double> b, out Vector128<double> luma, out Vector128<double> blue, out Vector128<double> red)
        {
            var y = (0.299 * r) + (0.587 * g) + (0.114 * b);
            luma = y - Vector128.Create(128.0);
            blue = (b - y) / Vector128.Create(1.772);
            red = (r - y) / Vector128.Create(1.402);
        }
    }

    /// <summary>
    /// Converts the pixels of whole 16-pixel groups with AVX2: the samples are deinterleaved as with
    /// <see cref="Vector128{T}"/>, widened to 32-bit integers (x64 has no 64-bit integer conversion before AVX-512) and
    /// converted four at a time.
    /// </summary>
    /// <returns>The number of pixels converted.</returns>
    internal static int ConvertRgbToYCbCrAvx2(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
    {
        var count = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), source.Length / 3);
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        var x = 0;
        for (; x + 16 <= count; x += 16)
        {
            var (r, g, b) = Deinterleave(ref unsafe(Unsafe.Add(ref sourceStart, 3 * x)));
            Convert8(Avx2.ConvertToVector256Int32(r), Avx2.ConvertToVector256Int32(g), Avx2.ConvertToVector256Int32(b), ref unsafe(Unsafe.Add(ref lumaStart, x)), ref unsafe(Unsafe.Add(ref blueStart, x)), ref unsafe(Unsafe.Add(ref redStart, x)));
            Convert8(
                Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(r, 8)),
                Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(g, 8)),
                Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(b, 8)),
                ref unsafe(Unsafe.Add(ref lumaStart, x + 8)), ref unsafe(Unsafe.Add(ref blueStart, x + 8)), ref unsafe(Unsafe.Add(ref redStart, x + 8)));
        }

        return x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Convert8(Vector256<int> r, Vector256<int> g, Vector256<int> b, ref float luma, ref float blue, ref float red)
        {
            Convert4(r.GetLower(), g.GetLower(), b.GetLower(), ref luma, ref blue, ref red);
            Convert4(r.GetUpper(), g.GetUpper(), b.GetUpper(), ref unsafe(Unsafe.Add(ref luma, 4)), ref unsafe(Unsafe.Add(ref blue, 4)), ref unsafe(Unsafe.Add(ref red, 4)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Convert4(Vector128<int> red8, Vector128<int> green8, Vector128<int> blue8, ref float luma, ref float blue, ref float red)
        {
            var r = Avx.ConvertToVector256Double(red8);
            var g = Avx.ConvertToVector256Double(green8);
            var b = Avx.ConvertToVector256Double(blue8);
            var y = (0.299 * r) + (0.587 * g) + (0.114 * b);
            unsafe { Avx.ConvertToVector128Single(y - Vector256.Create(128.0)).StoreUnsafe(ref luma); }
            unsafe { Avx.ConvertToVector128Single((b - y) / Vector256.Create(1.772)).StoreUnsafe(ref blue); }
            unsafe { Avx.ConvertToVector128Single((r - y) / Vector256.Create(1.402)).StoreUnsafe(ref red); }
        }
    }

    /// <summary>Deinterleaves 16 RGB pixels: each byte comes from one of three vectors (an index of 0xFF selects zero).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<byte> R, Vector128<byte> G, Vector128<byte> B) Deinterleave(ref byte source)
    {
        var v0 = unsafe(Vector128.LoadUnsafe(ref source));
        var v1 = unsafe(Vector128.LoadUnsafe(ref source, 16));
        var v2 = unsafe(Vector128.LoadUnsafe(ref source, 32));
        var r = Vector128.Shuffle(v0, Vector128.Create((byte)0, 3, 6, 9, 12, 15, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF))
            | Vector128.Shuffle(v1, Vector128.Create((byte)0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 2, 5, 8, 11, 14, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF))
            | Vector128.Shuffle(v2, Vector128.Create((byte)0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 4, 7, 10, 13));
        var g = Vector128.Shuffle(v0, Vector128.Create((byte)1, 4, 7, 10, 13, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF))
            | Vector128.Shuffle(v1, Vector128.Create((byte)0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0, 3, 6, 9, 12, 15, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF))
            | Vector128.Shuffle(v2, Vector128.Create((byte)0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 2, 5, 8, 11, 14));
        var b = Vector128.Shuffle(v0, Vector128.Create((byte)2, 5, 8, 11, 14, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF))
            | Vector128.Shuffle(v1, Vector128.Create((byte)0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 4, 7, 10, 13, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF))
            | Vector128.Shuffle(v2, Vector128.Create((byte)0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0, 3, 6, 9, 12, 15));
        return (r, g, b);
    }

    /// <summary>Gets a mask of the non-zero values among 64 coefficients: bit <c>k</c> is set when <c>coefficients[k]</c> is not zero.</summary>
    /// <param name="coefficients">The 64 coefficients.</param>
    internal static ulong GetNonZeroMask(ReadOnlySpan<int> coefficients)
    {
        coefficients = coefficients[..64];
        if (!Vector128.IsHardwareAccelerated)
            return GetNonZeroMaskScalar(coefficients);

        // The comparison results (all ones or zero) of 16 coefficients narrowed to 16 bytes, one mask bit each
        ref var start = ref unsafe(MemoryMarshal.GetReference(coefficients));
        var zeros = 0UL;
        for (var i = 0; i < 64; i += 16)
        {
            var first = Vector128.Narrow(Vector128.Equals(Load(ref start, i), Vector128<int>.Zero), Vector128.Equals(Load(ref start, i + 4), Vector128<int>.Zero));
            var second = Vector128.Narrow(Vector128.Equals(Load(ref start, i + 8), Vector128<int>.Zero), Vector128.Equals(Load(ref start, i + 12), Vector128<int>.Zero));
            zeros |= (ulong)Vector128.Narrow(first, second).ExtractMostSignificantBits() << i;
        }

        return ~zeros;

        static Vector128<int> Load(ref int start, int index) => unsafe(Vector128.LoadUnsafe(ref start, (nuint)index));
    }

    /// <summary>The scalar reference of <see cref="GetNonZeroMask"/>.</summary>
    internal static ulong GetNonZeroMaskScalar(ReadOnlySpan<int> coefficients)
    {
        var mask = 0UL;
        for (var k = 0; k < 64; k++)
        {
            if (coefficients[k] != 0)
            {
                mask |= 1UL << k;
            }
        }

        return mask;
    }

    /// <summary>
    /// Copies an 8x8 block of samples starting at (<paramref name="x0"/>, <paramref name="y0"/>) of an MCU-row plane. With
    /// subsampling factors above 1, each block sample is the mean of the <c>factorX x factorY</c> full-resolution samples
    /// it covers (box filter: the subsampled sample sits at the center of those samples, the JFIF chroma siting), summed in
    /// double precision from zero, row by row, then multiplied by <c>1 / (factorX factorY)</c>.
    /// </summary>
    /// <param name="plane">The plane.</param>
    /// <param name="planeWidth">The distance between two rows of the plane.</param>
    /// <param name="x0">The first column.</param>
    /// <param name="y0">The first row.</param>
    /// <param name="factorX">The horizontal subsampling factor.</param>
    /// <param name="factorY">The vertical subsampling factor.</param>
    /// <param name="block">The 64 samples of the block.</param>
    /// <remarks>
    /// With 128-bit hardware vectors, a horizontal factor of 2 (with a vertical factor of 1 or 2) computes two block samples
    /// at once with the same additions in the same order, and a factor of 1 copies 4 samples at a time.
    /// </remarks>
    internal static void ExtractBlock(ReadOnlySpan<float> plane, int planeWidth, int x0, int y0, int factorX, int factorY, Span<float> block)
    {
        if (Vector128.IsHardwareAccelerated && ((factorX == 1 && factorY == 1) || (factorX == 2 && factorY is 1 or 2)))
        {
            ExtractBlockVector(plane, planeWidth, x0, y0, factorX, factorY, block);
        }
        else
        {
            ExtractBlockScalar(plane, planeWidth, x0, y0, factorX, factorY, block);
        }
    }

    /// <summary>The scalar reference of <see cref="ExtractBlock"/>.</summary>
    internal static void ExtractBlockScalar(ReadOnlySpan<float> plane, int planeWidth, int x0, int y0, int factorX, int factorY, Span<float> block)
    {
        if (factorX == 1 && factorY == 1)
        {
            for (var y = 0; y < 8; y++)
            {
                plane.Slice(((y0 + y) * planeWidth) + x0, 8).CopyTo(block.Slice(y * 8, 8));
            }

            return;
        }

        var scale = 1d / (factorX * factorY);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0d;
                for (var dy = 0; dy < factorY; dy++)
                {
                    var line = plane.Slice(((y0 + (y * factorY) + dy) * planeWidth) + x0 + (x * factorX), factorX);
                    foreach (var sample in line)
                    {
                        sum += sample;
                    }
                }

                block[(y * 8) + x] = (float)(sum * scale);
            }
        }
    }

    private static void ExtractBlockVector(ReadOnlySpan<float> plane, int planeWidth, int x0, int y0, int factorX, int factorY, Span<float> block)
    {
        // Bounds are checked once here; the loops below use unchecked references
        var lastRow = ((y0 + (8 * factorY) - 1) * planeWidth) + x0;
        _ = plane.Slice(lastRow, 8 * factorX);
        _ = plane.Slice((y0 * planeWidth) + x0, 8 * factorX);
        ArgumentOutOfRangeException.ThrowIfLessThan(block.Length, 64);
        ref var source = ref unsafe(MemoryMarshal.GetReference(plane));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(block));
        if (factorX == 1)
        {
            for (var y = 0; y < 8; y++)
            {
                ref var row = ref unsafe(Unsafe.Add(ref source, ((y0 + y) * planeWidth) + x0));
                unsafe { Vector128.LoadUnsafe(ref row).StoreUnsafe(ref destination, (nuint)(y * 8)); }
                unsafe { Vector128.LoadUnsafe(ref row, 4).StoreUnsafe(ref destination, (nuint)((y * 8) + 4)); }
            }

            return;
        }

        var scale = Vector128.Create(1d / (factorX * factorY));
        for (var y = 0; y < 8; y++)
        {
            ref var first = ref unsafe(Unsafe.Add(ref source, ((y0 + (y * factorY)) * planeWidth) + x0));
            ref var second = ref unsafe(Unsafe.Add(ref first, factorY == 2 ? planeWidth : 0));
            for (var x = 0; x < 8; x += 4)
            {
                // Block samples x to x + 3 cover the plane samples 2x to 2x + 7: two at a time, (2x, 2x + 2) then (2x + 1, 2x + 3)
                var low = Mean(ref first, ref second, 2 * x, factorY == 2, scale);
                var high = Mean(ref first, ref second, (2 * x) + 4, factorY == 2, scale);
                unsafe { Vector128.Narrow(low, high).StoreUnsafe(ref destination, (nuint)((y * 8) + x)); }
            }
        }

        // The mean of the samples (2i, 2i + 1) of one or two rows, for the block samples i and i + 1 (i = start / 2)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<double> Mean(ref float first, ref float second, int start, bool twoRows, Vector128<double> scale)
        {
            var samples = unsafe(Vector128.LoadUnsafe(ref first, (nuint)start));
            var lower = Vector128.WidenLower(samples);
            var upper = Vector128.WidenUpper(samples);
            var sum = Vector128<double>.Zero + JpegIdct.InterleaveLower(lower, upper);
            sum += JpegIdct.InterleaveUpper(lower, upper);
            if (twoRows)
            {
                samples = unsafe(Vector128.LoadUnsafe(ref second, (nuint)start));
                lower = Vector128.WidenLower(samples);
                upper = Vector128.WidenUpper(samples);
                sum += JpegIdct.InterleaveLower(lower, upper);
                sum += JpegIdct.InterleaveUpper(lower, upper);
            }

            return sum * scale;
        }
    }

    /// <summary>
    /// The encoding state of one JPEG. The frame operation writes SOI and the JFIF APP0
    /// segment, then one metadata segment per <see cref="Encode"/> call (<see cref="JpegMetadataWriter"/>), the tables (DQT,
    /// SOF0, DHT) and the scan header, then the entropy-coded MCUs in bounded steps: each call loads the source rows of an
    /// MCU row (converted and level-shifted into single-precision Y, Cb and Cr rows, the canvas padded to whole MCUs by edge
    /// replication) and codes MCUs until the output buffer should be flushed. Completion writes EOI. Settings, the
    /// bit-depth policy, metadata sizes and the working buffers fail on creation, before any output; non-opaque pixels
    /// without a background fail in <see cref="ValidateFrame"/>, before anything of the frame is written.
    /// </summary>
    /// <remarks>
    /// A progressive JPEG (<see cref="JpegEncodingProcess.Progressive"/>) has the same quantized coefficients, written in
    /// several scans. After DQT and SOF2, each call transforms one MCU row into the coefficients of the whole image
    /// (<see cref="JpegCoefficientPlane"/>, allocated on creation); the source rows are not needed afterwards. Each scan of
    /// <see cref="JpegProgressiveScanEncoder.GetScript"/> is then coded twice from the coefficients: once to count its
    /// symbols, one row of blocks per call, then, after the DHT segment of the tables built from the counts and the scan
    /// header, to write it in bounded steps.
    /// <para>
    /// With <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> above 1 (the configuration of the saved image), the MCU
    /// rows of a frame of at least <see cref="RowBands.MinPixelsForParallelism"/> pixels are converted, transformed and
    /// quantized by several workers, a batch of <see cref="RowsPerWorker"/> rows per worker at a time: an MCU row reads only
    /// its own rows of the image (the last one repeated below it), and each worker has its own sample rows and DCT scratch
    /// (<see cref="TransformWorker"/>, rented with the session). The quantized coefficients of a baseline batch are kept
    /// until they are coded; the coefficients of a progressive frame are those of the whole image anyway. The entropy coding
    /// stays on the calling thread, in the order of one worker, so the bytes never depend on the number of workers.
    /// </para>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const byte MarkerSoi = 0xD8;
        private const byte MarkerEoi = 0xD9;
        private const byte MarkerSof0 = 0xC0;
        private const byte MarkerSof2 = 0xC2;
        private const byte MarkerDht = 0xC4;
        private const byte MarkerDqt = 0xDB;
        private const byte MarkerSos = 0xDA;

        private readonly JpegEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _workFormat;
        private readonly Rgba64? _background;
        private readonly bool _grayscale;
        private readonly int _h;
        private readonly int _v;
        private readonly int _width;
        private readonly int _height;
        private readonly int _mcusX;
        private readonly int _mcusY;
        private readonly int _planeWidth;
        private readonly ushort[] _luminanceQuantization = new ushort[64];
        private readonly ushort[] _chrominanceQuantization = new ushort[64];
        private readonly JpegHuffmanEncoder _luminanceDc;
        private readonly JpegHuffmanEncoder _luminanceAc;
        private readonly JpegHuffmanEncoder? _chrominanceDc;
        private readonly JpegHuffmanEncoder? _chrominanceAc;
        // The number of MCU rows that each worker transforms in a batch
        private const int RowsPerWorker = 4;

        private readonly JpegMetadataWriter _metadata;
        private readonly int[] _predictions = new int[3];
        private readonly JpegProgressiveScanEncoder? _scanEncoder;
        private readonly int _blocksPerMcu;
        private readonly int _batchRows;
        private JpegCoefficientPlane?[] _coefficientPlanes = [];

        // The buffers of the workers that transform MCU rows: one for one worker, which codes each block as it is transformed
        private TransformWorker[] _transformWorkers = [];

        // With several workers, the quantized coefficients (zig-zag order) of the MCU rows of a baseline batch
        private PooledBuffer? _batch;
        private int _batchStart;
        private int _batchEnd;
        private ImageFrame? _frame;
        private Step _step;
        private int _metadataIndex;
        private int _mcuRow;
        private int _mcuX;
        private bool _rowLoaded;
        private bool _frameWritten;
        private JpegBitWriter _bits;
        private int _scanIndex;
        private ScanPass _scanPass;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (JpegEncoder)options.Encoder;
            _sourceFormat = options.PixelFormat;
            if (PixelFormats.GetBitsPerComponent(_sourceFormat) > 8 && !_encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"JPEG output stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set JpegEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Jpeg, "Bit depth reduction");

            (_grayscale, _h, _v) = GetSampling(_sourceFormat, _encoder.ChromaSubsampling);
            _workFormat = _grayscale ? PixelFormat.Gray8 : PixelFormat.Rgb24;
            _background = PixelFormats.HasAlpha(_sourceFormat) ? _encoder.BackgroundColor : null;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _mcusX = (_width + (8 * _h) - 1) / (8 * _h);
            _mcusY = (_height + (8 * _v) - 1) / (8 * _v);
            _planeWidth = _mcusX * 8 * _h;
            _blocksPerMcu = _grayscale ? 1 : (_h * _v) + 2;

            JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.LuminanceQuantization, _encoder.Quality, _luminanceQuantization);
            JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.ChrominanceQuantization, _encoder.Quality, _chrominanceQuantization);
            _luminanceDc = new JpegHuffmanEncoder(JpegEncodingTables.LuminanceDcBits, JpegEncodingTables.LuminanceDcValues);
            _luminanceAc = new JpegHuffmanEncoder(JpegEncodingTables.LuminanceAcBits, JpegEncodingTables.LuminanceAcValues);
            if (!_grayscale)
            {
                _chrominanceDc = new JpegHuffmanEncoder(JpegEncodingTables.ChrominanceDcBits, JpegEncodingTables.ChrominanceDcValues);
                _chrominanceAc = new JpegHuffmanEncoder(JpegEncodingTables.ChrominanceAcBits, JpegEncodingTables.ChrominanceAcValues);
            }

            _metadata = new JpegMetadataWriter(options.Metadata);

            // One MCU row of samples per component, at full resolution (chroma is box-filtered when blocks are extracted)
            var planeBytes = (long)_planeWidth * 8 * _v * sizeof(float);
            var rowBytes = (long)_width * PixelFormats.GetBytesPerPixel(_workFormat);
            if (planeBytes > CheckedSizes.MaxBufferLength || rowBytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Scope.Limits);

            var scope = options.Scope;
            var workerCount = Math.Min(RowBands.GetWorkerCount(options.CanvasSize, options.Configuration.MaxDegreeOfParallelism), _mcusY);
            _batchRows = Math.Min(_mcusY, workerCount * RowsPerWorker);
            try
            {
                // Every buffer of every worker is rented now, on the calling thread
                _transformWorkers = new TransformWorker[workerCount];
                for (var i = 0; i < workerCount; i++)
                {
                    _transformWorkers[i] = new TransformWorker(scope, _sourceFormat != _workFormat ? (int)rowBytes : 0, (int)planeBytes, _grayscale);
                }

                if (workerCount > 1 && _encoder.Process != JpegEncodingProcess.Progressive)
                {
                    var batchBytes = (long)_batchRows * _mcusX * _blocksPerMcu * 64 * sizeof(short);
                    if (batchBytes > CheckedSizes.MaxBufferLength)
                        throw CheckedSizes.CreateOverflowException(scope.Limits);

                    _batch = scope.Rent((int)batchBytes, AllocationKind.Temporary, clear: false);
                }

                if (_encoder.Process == JpegEncodingProcess.Progressive)
                {
                    // The grid of a scan of one component alone only has the blocks that hold samples of the image
                    _scanEncoder = new JpegProgressiveScanEncoder();
                    _coefficientPlanes = new JpegCoefficientPlane?[_grayscale ? 1 : 3];
                    _coefficientPlanes[0] = new JpegCoefficientPlane(scope, _mcusX * _h, _mcusY * _v, (_width + 7) / 8, (_height + 7) / 8);
                    for (var c = 1; c < _coefficientPlanes.Length; c++)
                    {
                        _coefficientPlanes[c] = new JpegCoefficientPlane(scope, _mcusX, _mcusY, (((_width + _h - 1) / _h) + 7) / 8, (((_height + _v - 1) / _v) + 7) / 8);
                    }
                }
            }
            catch
            {
                ReleaseBuffers();
                throw;
            }
        }

        private enum Step
        {
            None,
            Header,
            Metadata,
            Tables,
            Scan,
            Transform,
            ProgressiveScans,
            Trailer,
        }

        /// <summary>The passes over the blocks of a progressive scan.</summary>
        private enum ScanPass
        {
            /// <summary>The scan is not started.</summary>
            None,

            /// <summary>The symbols are counted.</summary>
            Count,

            /// <summary>The tables and the scan header are written.</summary>
            Header,

            /// <summary>The entropy-coded data is written.</summary>
            Write,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // Alpha is never discarded silently: without a background every pixel must be opaque.
            // The scan runs before anything of the frame is written, so a rejection leaves the writer usable.
            if (!PixelFormats.HasAlpha(_sourceFormat) || _background is not null)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                var x = PixelConverter.IndexOfNonOpaque(_sourceFormat, lease.GetRowBytes(y));
                if (x >= 0)
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"JPEG output has no alpha channel and the pixel ({x}, {y}) is not fully opaque. Set JpegEncoder.BackgroundColor to composite the pixels over an opaque color, or flatten the image explicitly."), ImageFormat.Jpeg, "Alpha removal");
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("JPEG output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0 || _frameWritten)
                throw new InvalidOperationException("JPEG output stores a single frame.");

            _frame = frame;
            _frameWritten = true;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount)
        {
            if (frameCount != 1 || _step != Step.None)
                throw new InvalidOperationException("JPEG output stores exactly one frame.");

            _step = Step.Trailer;
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                    WriteMarker(output, MarkerSoi);
                    JpegMetadataWriter.WriteJfif(output, Options.Metadata);
                    _step = Step.Metadata;
                    return false;

                case Step.Metadata:
                    if (_metadataIndex < _metadata.Count)
                    {
                        _metadata.Write(_metadataIndex++, output);
                        return false;
                    }

                    _step = Step.Tables;
                    goto case Step.Tables;

                case Step.Tables:
                    WriteQuantizationTables(output);
                    if (_scanEncoder is not null)
                    {
                        WriteFrameHeader(output, MarkerSof2);
                        _step = Step.Transform;
                        return false;
                    }

                    WriteFrameHeader(output, MarkerSof0);
                    WriteHuffmanTables(output);
                    WriteScanHeader(output, firstComponent: 0, _grayscale ? 1 : 3, dcTables: true, acTables: true, spectralStart: 0, spectralEnd: 63, approximation: 0);
                    _step = Step.Scan;
                    return false;

                case Step.Transform:
                    TransformMcuRow();
                    CancellationToken.ThrowIfCancellationRequested();
                    if (++_mcuRow < _mcusY)
                        return false;

                    // Every scan is coded from the coefficients: the frame and the sample rows are not needed any more
                    _frame = null;
                    ReleaseSampleBuffers();
                    _mcuRow = 0;
                    _step = Step.ProgressiveScans;
                    return false;

                case Step.ProgressiveScans:
                    if (!EncodeProgressiveScans(output))
                        return false;

                    _step = Step.None;
                    ReleaseBuffers();
                    return true;

                case Step.Scan:
                    if (!EncodeScan(output))
                        return false;

                    // The frame is no longer referenced once its operation completes; the buffers are not needed any more
                    _frame = null;
                    _step = Step.None;
                    ReleaseBuffers();
                    return true;

                case Step.Trailer:
                    WriteMarker(output, MarkerEoi);
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No JPEG encoding operation is in progress.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            _frame = null;
            ReleaseBuffers();
            base.Dispose(disposing);
        }

        private static void WriteMarker(ImageOutputBuffer output, byte marker) => output.Write([0xFF, marker]);

        private static void WriteHuffmanTable(Span<byte> destination, ref int offset, int tableClass, int identifier, JpegHuffmanEncoder table)
        {
            destination[offset++] = (byte)((tableClass << 4) | identifier);
            table.Bits.Span.CopyTo(destination[offset..]);
            offset += 16;
            table.Values.Span.CopyTo(destination[offset..]);
            offset += table.Values.Length;
        }

        private void ReleaseBuffers()
        {
            ReleaseSampleBuffers();
            foreach (var plane in _coefficientPlanes)
            {
                plane?.Dispose();
            }

            _coefficientPlanes = [];
        }

        private void ReleaseSampleBuffers()
        {
            foreach (var worker in _transformWorkers)
            {
                worker?.Dispose();
            }

            _transformWorkers = [];
            _batch?.Dispose();
            _batch = null;
        }

        private void WriteQuantizationTables(ImageOutputBuffer output)
        {
            // 8-bit precision (Pq = 0): the quality scaling clamps every value to 1..255; values in zig-zag order
            var count = _grayscale ? 1 : 2;
            Span<byte> payload = stackalloc byte[65 * 2];
            var zigZag = JpegIdct.ZigZag;
            for (var t = 0; t < count; t++)
            {
                var table = t == 0 ? _luminanceQuantization : _chrominanceQuantization;
                payload[t * 65] = (byte)t;
                for (var k = 0; k < 64; k++)
                {
                    payload[(t * 65) + 1 + k] = (byte)table[zigZag[k]];
                }
            }

            JpegMetadataWriter.WriteSegment(output, MarkerDqt, payload[..(65 * count)]);
        }

        private void WriteFrameHeader(ImageOutputBuffer output, byte marker)
        {
            // Precision 8, height, width, components (identifiers 1, 2, 3 as JFIF requires; chroma 1x1, tables 1)
            var count = _grayscale ? 1 : 3;
            Span<byte> payload = stackalloc byte[6 + (3 * 3)];
            payload[0] = 8;
            BinaryPrimitives.WriteUInt16BigEndian(payload[1..], (ushort)_height);
            BinaryPrimitives.WriteUInt16BigEndian(payload[3..], (ushort)_width);
            payload[5] = (byte)count;
            for (var c = 0; c < count; c++)
            {
                payload[6 + (3 * c)] = (byte)(c + 1);
                payload[7 + (3 * c)] = c == 0 ? (byte)((_h << 4) | _v) : (byte)0x11;
                payload[8 + (3 * c)] = c == 0 ? (byte)0 : (byte)1;
            }

            JpegMetadataWriter.WriteSegment(output, marker, payload[..(6 + (3 * count))]);
        }

        private void WriteHuffmanTables(ImageOutputBuffer output)
        {
            Span<byte> payload = stackalloc byte[4 * (17 + 256)];
            var offset = 0;
            WriteHuffmanTable(payload, ref offset, tableClass: 0, identifier: 0, _luminanceDc);
            WriteHuffmanTable(payload, ref offset, tableClass: 1, identifier: 0, _luminanceAc);
            if (!_grayscale)
            {
                WriteHuffmanTable(payload, ref offset, tableClass: 0, identifier: 1, _chrominanceDc!);
                WriteHuffmanTable(payload, ref offset, tableClass: 1, identifier: 1, _chrominanceAc!);
            }

            JpegMetadataWriter.WriteSegment(output, MarkerDht, payload[..offset]);
        }

        /// <summary>Writes the header of a scan of consecutive components (T.81 B.2.3).</summary>
        /// <param name="output">The output.</param>
        /// <param name="firstComponent">The index of the first component of the scan.</param>
        /// <param name="count">The number of components of the scan.</param>
        /// <param name="dcTables">Whether the scan uses DC tables (0 for the luminance, 1 for the chrominance).</param>
        /// <param name="acTables">Whether the scan uses AC tables (0 for the luminance, 1 for the chrominance).</param>
        /// <param name="spectralStart">Ss: 0 for a baseline scan.</param>
        /// <param name="spectralEnd">Se: 63 for a baseline scan.</param>
        /// <param name="approximation">Ah and Al: 0 for a baseline scan.</param>
        private static void WriteScanHeader(ImageOutputBuffer output, int firstComponent, int count, bool dcTables, bool acTables, int spectralStart, int spectralEnd, int approximation)
        {
            Span<byte> payload = stackalloc byte[1 + (2 * 3) + 3];
            payload[0] = (byte)count;
            for (var i = 0; i < count; i++)
            {
                var component = firstComponent + i;
                var table = JpegProgressiveScanEncoder.GetTableIndex(component);
                payload[1 + (2 * i)] = (byte)(component + 1);
                payload[2 + (2 * i)] = (byte)((dcTables ? table << 4 : 0) | (acTables ? table : 0));
            }

            var tail = 1 + (2 * count);
            payload[tail] = (byte)spectralStart;
            payload[tail + 1] = (byte)spectralEnd;
            payload[tail + 2] = (byte)approximation;
            JpegMetadataWriter.WriteSegment(output, MarkerSos, payload[..(tail + 3)]);
        }

        /// <summary>Transforms the blocks of the next MCU row, or of the next batch of rows with several workers, into the coefficients of the image.</summary>
        private void TransformMcuRow()
        {
            var frame = _frame ?? throw new InvalidOperationException("No JPEG frame is being written.");
            if (_transformWorkers.Length > 1)
            {
                using var lease = frame.GetStorage().AcquireLease();
                var end = Math.Min(_mcusY, _mcuRow + _batchRows);
                TransformRows(lease.Storage, _mcuRow, end, (worker, row, mcuX, block) => GetProgressiveBlock(row, mcuX, block));

                // The last row of the batch is counted by the caller
                _mcuRow = end - 1;
                return;
            }

            var single = _transformWorkers[0];
            using (var lease = frame.GetStorage().AcquireLease())
            {
                LoadMcuRow(single, lease.Storage, _mcuRow);
            }

            for (var mcuX = 0; mcuX < _mcusX; mcuX++)
            {
                for (var block = 0; block < _blocksPerMcu; block++)
                {
                    TransformBlock(single, mcuX, block, GetProgressiveBlock(_mcuRow, mcuX, block));
                }
            }
        }

        /// <summary>Gets the coefficients of a block of an MCU of a progressive frame: the luma blocks in raster order, then Cb and Cr.</summary>
        private Span<short> GetProgressiveBlock(int row, int mcuX, int block)
        {
            var lumaBlocks = _h * _v;
            return block < lumaBlocks
                ? _coefficientPlanes[0]!.GetBlock((row * _v) + (block / _h), (mcuX * _h) + (block % _h))
                : _coefficientPlanes[block - lumaBlocks + 1]!.GetBlock(row, mcuX);
        }

        /// <summary>Gets the coefficients of a block of an MCU of the baseline batch.</summary>
        private Span<short> GetBatchBlock(int row, int mcuX, int block)
        {
            var index = ((((row - _batchStart) * _mcusX) + mcuX) * _blocksPerMcu) + block;
            return unsafe(MemoryMarshal.Cast<byte, short>(_batch!.RawBuffer.AsSpan(index * 64 * sizeof(short), 64 * sizeof(short))));
        }

        /// <summary>
        /// Converts, transforms and quantizes the MCU rows from <paramref name="first"/> to <paramref name="end"/> (excluded)
        /// with the workers, each one a contiguous range of rows: the error is the one of the first row that fails (a
        /// cancellation first).
        /// </summary>
        private void TransformRows(PixelStorage storage, int first, int end, BlockDestination destination)
        {
            var count = end - first;
            var workers = Math.Min(_transformWorkers.Length, count);
            var cancellationToken = CancellationToken;
            RowBands.RunParallelInBandOrder(workers, index =>
            {
                var worker = _transformWorkers[index];
                var last = first + RowBands.GetBandStart(count, index + 1, workers);
                for (var row = first + RowBands.GetBandStart(count, index, workers); row < last; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LoadMcuRow(worker, storage, row);
                    for (var mcuX = 0; mcuX < _mcusX; mcuX++)
                    {
                        for (var block = 0; block < _blocksPerMcu; block++)
                        {
                            TransformBlock(worker, mcuX, block, destination(worker, row, mcuX, block));
                        }
                    }
                }
            });
        }

        /// <summary>Transforms and quantizes a block of an MCU of the loaded row of a worker, and stores its coefficients (zig-zag order).</summary>
        private void TransformBlock(TransformWorker worker, int mcuX, int block, Span<short> destination)
        {
            var coefficients = worker.Coefficients.AsSpan();
            LoadBlock(worker, mcuX, block);
            JpegForwardDct.TransformAndQuantize(worker.Block, block < _h * _v ? _luminanceQuantization : _chrominanceQuantization, worker.Scratch, coefficients);
            for (var k = 0; k < 64; k++)
            {
                destination[k] = (short)coefficients[k];
            }
        }

        /// <summary>Copies the samples of a block of an MCU of the loaded row of a worker: the luma blocks in raster order, then the box-filtered Cb and Cr blocks.</summary>
        private void LoadBlock(TransformWorker worker, int mcuX, int block)
        {
            var x0 = mcuX * 8 * _h;
            var lumaBlocks = _h * _v;
            if (block < lumaBlocks)
            {
                ExtractBlock(worker.PlaneY, _planeWidth, x0 + (block % _h * 8), block / _h * 8, factorX: 1, factorY: 1, worker.Block);
            }
            else
            {
                ExtractBlock(block == lumaBlocks ? worker.PlaneCb : worker.PlaneCr, _planeWidth, x0, 0, _h, _v, worker.Block);
            }
        }

        /// <returns><see langword="true"/> when the last scan is written.</returns>
        private bool EncodeProgressiveScans(ImageOutputBuffer output)
        {
            var scanEncoder = _scanEncoder!;
            var scan = JpegProgressiveScanEncoder.GetScript(_grayscale)[_scanIndex];

            // A DC refinement scan has no Huffman-coded symbol: nothing to count and no table
            var hasTables = scan.Ss > 0 || scan.Ah == 0;
            if (_scanPass == ScanPass.None)
            {
                _scanPass = hasTables ? ScanPass.Count : ScanPass.Header;
                scanEncoder.BeginScan(scan, counting: hasTables);
            }

            var interleaved = scan.ComponentCount > 1;
            var rows = interleaved ? _mcusY : _coefficientPlanes[scan.FirstComponent]!.ScanBlockRows;
            var columns = interleaved ? _mcusX : _coefficientPlanes[scan.FirstComponent]!.ScanBlocksPerLine;
            var position = 0;
            switch (_scanPass)
            {
                case ScanPass.Count:
                    for (var x = 0; x < columns; x++)
                    {
                        CodeProgressiveMcu(scan, _mcuRow, x, default, ref position);
                    }

                    if (++_mcuRow < rows)
                        return false;

                    scanEncoder.EndScan(default, ref position);
                    _mcuRow = 0;
                    _scanPass = ScanPass.Header;
                    return false;

                case ScanPass.Header:
                    if (hasTables)
                    {
                        WriteProgressiveTables(output, scan);
                    }

                    WriteScanHeader(output, scan.FirstComponent, scan.ComponentCount, dcTables: scan.Ss == 0 && scan.Ah == 0, acTables: scan.Ss > 0, scan.Ss, scan.Se, (scan.Ah << 4) | scan.Al);
                    scanEncoder.BeginScan(scan, counting: false);
                    _scanPass = ScanPass.Write;
                    return false;
            }

            var blocksPerMcu = interleaved ? (_h * _v) + 2 : 1;
            var coded = 0;
            while (_mcuRow < rows)
            {
                while (_mcuX < columns)
                {
                    // Bounded output per call: stop once the buffer should be flushed (at least one MCU per call)
                    if (coded > 0 && output.ShouldFlush)
                        return false;

                    var span = output.GetSpan(blocksPerMcu * JpegProgressiveScanEncoder.MaxBlockBytes);
                    position = 0;
                    CodeProgressiveMcu(scan, _mcuRow, _mcuX, span, ref position);
                    output.Advance(position);
                    _mcuX++;
                    coded++;
                }

                _mcuX = 0;
                _mcuRow++;
                CancellationToken.ThrowIfCancellationRequested();
            }

            var end = output.GetSpan(JpegProgressiveScanEncoder.MaxBlockBytes);
            position = 0;
            scanEncoder.EndScan(end, ref position);
            output.Advance(position);
            _mcuRow = 0;
            _scanPass = ScanPass.None;
            _scanIndex++;
            return _scanIndex == JpegProgressiveScanEncoder.GetScript(_grayscale).Length;
        }

        /// <summary>Codes one MCU of a progressive scan: the blocks of every component for an interleaved scan, else one block (T.81 A.2).</summary>
        private void CodeProgressiveMcu(JpegProgressiveScan scan, int row, int column, Span<byte> destination, ref int position)
        {
            var scanEncoder = _scanEncoder!;
            if (scan.ComponentCount == 1)
            {
                scanEncoder.CodeBlock(scan.FirstComponent, _coefficientPlanes[scan.FirstComponent]!.GetBlock(row, column), destination, ref position);
                return;
            }

            var luminance = _coefficientPlanes[0]!;
            for (var by = 0; by < _v; by++)
            {
                for (var bx = 0; bx < _h; bx++)
                {
                    scanEncoder.CodeBlock(0, luminance.GetBlock((row * _v) + by, (column * _h) + bx), destination, ref position);
                }
            }

            scanEncoder.CodeBlock(1, _coefficientPlanes[1]!.GetBlock(row, column), destination, ref position);
            scanEncoder.CodeBlock(2, _coefficientPlanes[2]!.GetBlock(row, column), destination, ref position);
        }

        /// <summary>Writes the DHT segment of the tables of a progressive scan: DC tables for a DC first scan, the AC table of the component otherwise.</summary>
        private void WriteProgressiveTables(ImageOutputBuffer output, JpegProgressiveScan scan)
        {
            var scanEncoder = _scanEncoder!;
            Span<byte> payload = stackalloc byte[2 * (17 + 256)];
            var offset = 0;
            var tableClass = scan.Ss == 0 ? 0 : 1;
            var first = JpegProgressiveScanEncoder.GetTableIndex(scan.FirstComponent);
            var last = JpegProgressiveScanEncoder.GetTableIndex(scan.FirstComponent + scan.ComponentCount - 1);
            for (var table = first; table <= last; table++)
            {
                WriteHuffmanTable(payload, ref offset, tableClass, table, scanEncoder.GetTable(table));
            }

            JpegMetadataWriter.WriteSegment(output, MarkerDht, payload[..offset]);
        }

        /// <returns><see langword="true"/> when the last MCU was coded and the entropy-coded segment padded.</returns>
        private bool EncodeScan(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No JPEG frame is being written.");
            var blocksPerMcu = _blocksPerMcu;
            var batched = _batch is not null;
            using (var lease = frame.GetStorage().AcquireLease())
            {
                var coded = 0;
                while (_mcuRow < _mcusY)
                {
                    if (!_rowLoaded)
                    {
                        if (!batched)
                        {
                            LoadMcuRow(_transformWorkers[0], lease.Storage, _mcuRow);
                        }
                        else if (_mcuRow >= _batchEnd)
                        {
                            // The workers transform the next batch of rows; the blocks are coded from their coefficients
                            _batchStart = _mcuRow;
                            _batchEnd = Math.Min(_mcusY, _mcuRow + _batchRows);
                            TransformRows(lease.Storage, _batchStart, _batchEnd, (worker, row, mcuX, block) => GetBatchBlock(row, mcuX, block));
                        }

                        _rowLoaded = true;
                    }

                    while (_mcuX < _mcusX)
                    {
                        // Bounded output per call: stop once the buffer should be flushed (at least one MCU per call)
                        if (coded > 0 && output.ShouldFlush)
                            return false;

                        var span = output.GetSpan(blocksPerMcu * MaxBlockBytes);
                        var length = batched ? EncodeStoredMcu(_mcuRow, _mcuX, span) : EncodeMcu(_mcuX, span);
                        output.Advance(length);
                        _mcuX++;
                        coded++;
                    }

                    _mcuX = 0;
                    _mcuRow++;
                    _rowLoaded = false;
                    CancellationToken.ThrowIfCancellationRequested();
                }
            }

            // Pad the last byte with 1-bits (T.81 F.1.2.3)
            var end = output.GetSpan(2);
            var endPosition = 0;
            _bits.Pad(end, ref endPosition);
            output.Advance(endPosition);
            return true;
        }

        /// <summary>
        /// Loads the rows of one MCU row: each source row (the last row replicated below the image) is converted to 8-bit RGB
        /// or gray (alpha flattened onto the background and 16-bit samples reduced, by the shared converter), then to
        /// level-shifted Y, Cb, Cr samples, the last column replicated to the padded width.
        /// </summary>
        private void LoadMcuRow(TransformWorker worker, PixelStorage storage, int mcuRow)
        {
            var rows = 8 * _v;
            var planeY = worker.PlaneY;
            var planeCb = worker.PlaneCb;
            var planeCr = worker.PlaneCr;
            for (var r = 0; r < rows; r++)
            {
                var y = Math.Min((mcuRow * rows) + r, _height - 1);
                ReadOnlySpan<byte> source = storage.GetRowSpanCore(y);
                if (worker.Row is { } row)
                {
                    var converted = row.Span;
                    PixelConverter.ConvertRow(_sourceFormat, source, _workFormat, converted, _background, ImageFormat.Jpeg);
                    source = converted;
                }

                var offset = r * _planeWidth;
                if (_grayscale)
                {
                    ConvertGrayRow(source, planeY.Slice(offset, _planeWidth));
                }
                else
                {
                    ConvertColorRow(source, planeY.Slice(offset, _planeWidth), planeCb.Slice(offset, _planeWidth), planeCr.Slice(offset, _planeWidth));
                }
            }
        }

        private void ConvertGrayRow(ReadOnlySpan<byte> source, Span<float> luma)
        {
            for (var x = 0; x < _width; x++)
            {
                luma[x] = source[x] - 128f;
            }

            luma[_width..].Fill(luma[_width - 1]);
        }

        /// <summary>
        /// JFIF 1.02 full-range YCbCr (ITU-R BT.601 luma weights): <c>Y = 0.299 R + 0.587 G + 0.114 B</c>,
        /// <c>Cb = (B - Y) / 1.772 + 128</c>, <c>Cr = (R - Y) / 1.402 + 128</c>, computed in double precision and stored
        /// unrounded (single precision) after the level shift of 128, so the only rounding before the DCT is the conversion
        /// to single precision.
        /// </summary>
        private void ConvertColorRow(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        {
            ConvertRgbToYCbCr(source, luma[.._width], blue[.._width], red[.._width]);
            luma[_width..].Fill(luma[_width - 1]);
            blue[_width..].Fill(blue[_width - 1]);
            red[_width..].Fill(red[_width - 1]);
        }

        /// <summary>Transforms and codes an MCU of the loaded row of the only worker.</summary>
        private int EncodeMcu(int mcuX, Span<byte> destination)
        {
            var position = 0;
            var worker = _transformWorkers[0];
            var planeY = worker.PlaneY;
            var x0 = mcuX * 8 * _h;
            for (var by = 0; by < _v; by++)
            {
                for (var bx = 0; bx < _h; bx++)
                {
                    ExtractBlock(planeY, _planeWidth, x0 + (bx * 8), by * 8, factorX: 1, factorY: 1, worker.Block);
                    TransformAndEncodeBlock(worker, destination, ref position, component: 0, _luminanceQuantization, _luminanceDc, _luminanceAc);
                }
            }

            if (!_grayscale)
            {
                ExtractBlock(worker.PlaneCb, _planeWidth, x0, 0, _h, _v, worker.Block);
                TransformAndEncodeBlock(worker, destination, ref position, component: 1, _chrominanceQuantization, _chrominanceDc!, _chrominanceAc!);
                ExtractBlock(worker.PlaneCr, _planeWidth, x0, 0, _h, _v, worker.Block);
                TransformAndEncodeBlock(worker, destination, ref position, component: 2, _chrominanceQuantization, _chrominanceDc!, _chrominanceAc!);
            }

            return position;
        }

        private void TransformAndEncodeBlock(TransformWorker worker, Span<byte> destination, ref int position, int component, ReadOnlySpan<ushort> quantization, JpegHuffmanEncoder dc, JpegHuffmanEncoder ac)
        {
            var coefficients = worker.Coefficients.AsSpan();
            JpegForwardDct.TransformAndQuantize(worker.Block, quantization, worker.Scratch, coefficients);
            EncodeBlock(destination, ref position, coefficients, component, dc, ac);
        }

        /// <summary>Codes an MCU of the baseline batch from its stored coefficients.</summary>
        private int EncodeStoredMcu(int row, int mcuX, Span<byte> destination)
        {
            var position = 0;
            var coefficients = _transformWorkers[0].Coefficients.AsSpan();
            for (var block = 0; block < _blocksPerMcu; block++)
            {
                var stored = GetBatchBlock(row, mcuX, block);
                for (var k = 0; k < 64; k++)
                {
                    coefficients[k] = stored[k];
                }

                var component = block < _h * _v ? 0 : block - (_h * _v) + 1;
                EncodeBlock(destination, ref position, coefficients, component, component == 0 ? _luminanceDc : _chrominanceDc!, component == 0 ? _luminanceAc : _chrominanceAc!);
            }

            return position;
        }

        /// <summary>Codes the quantized coefficients (zig-zag order) of a block of a component.</summary>
        private void EncodeBlock(Span<byte> destination, ref int position, Span<int> coefficients, int component, JpegHuffmanEncoder dc, JpegHuffmanEncoder ac)
        {
            // DC: difference with the previous block of the component, size category then the additional bits (T.81 F.1.2.1)
            var difference = coefficients[0] - _predictions[component];
            _predictions[component] = coefficients[0];
            WriteValue(destination, ref position, dc, symbolHigh: 0, difference);

            // AC: run lengths of zeros (ZRL for 16), EOB after the last nonzero coefficient (T.81 F.1.2.2); the nonzero
            // coefficients are visited through a bit mask
            var mask = GetNonZeroMask(coefficients) & ~1UL;
            var previous = 0;
            while (mask != 0)
            {
                var k = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                var run = k - previous - 1;
                while (run > 15)
                {
                    WriteSymbol(destination, ref position, ac, 0xF0);
                    run -= 16;
                }

                WriteValue(destination, ref position, ac, run, coefficients[k]);
                previous = k;
            }

            if (previous < 63)
            {
                WriteSymbol(destination, ref position, ac, 0x00);
            }
        }

        /// <summary>Writes the symbol <c>(symbolHigh &lt;&lt; 4) | SSSS</c> for a value, then its SSSS additional bits (negative values as <c>value - 1</c>), as one write.</summary>
        private void WriteValue(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbolHigh, int value)
        {
            var magnitude = (uint)Math.Abs(value);
            var size = magnitude == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(magnitude);
            var bits = value < 0 ? (uint)(value - 1) & ((1u << size) - 1) : (uint)value;
            _bits.WriteSymbolAndBits(destination, ref position, table, (symbolHigh << 4) | size, bits, size);
        }

        private void WriteSymbol(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbol) => _bits.WriteSymbolAndBits(destination, ref position, table, symbol, 0, 0);

        /// <summary>
        /// The buffers of a worker that transforms MCU rows: the source row converted to the working format, one MCU row of
        /// level-shifted samples per component, and the block, scratch and coefficients of the forward DCT.
        /// </summary>
        private sealed class TransformWorker : IDisposable
        {
            private PooledBuffer? _row;
            private PooledBuffer? _planeY;
            private PooledBuffer? _planeCb;
            private PooledBuffer? _planeCr;

            /// <param name="scope">The scope the buffers are rented from.</param>
            /// <param name="rowBytes">The size of the converted source row, or 0 when the source rows are used as they are.</param>
            /// <param name="planeBytes">The size of an MCU row of samples of a component.</param>
            /// <param name="grayscale">Whether there is only the luma plane.</param>
            public TransformWorker(AllocationScope scope, int rowBytes, int planeBytes, bool grayscale)
            {
                try
                {
                    if (rowBytes > 0)
                    {
                        _row = scope.Rent(rowBytes, AllocationKind.Temporary, clear: false);
                    }

                    _planeY = scope.Rent(planeBytes, AllocationKind.Temporary, clear: false);
                    if (!grayscale)
                    {
                        _planeCb = scope.Rent(planeBytes, AllocationKind.Temporary, clear: false);
                        _planeCr = scope.Rent(planeBytes, AllocationKind.Temporary, clear: false);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            /// <summary>Gets the source row converted to the working format, or <see langword="null"/> when the source rows are used as they are.</summary>
            public PooledBuffer? Row => _row;

            public Span<float> PlaneY => unsafe(MemoryMarshal.Cast<byte, float>(_planeY!.Span));

            public Span<float> PlaneCb => _planeCb is null ? default : unsafe(MemoryMarshal.Cast<byte, float>(_planeCb.Span));

            public Span<float> PlaneCr => _planeCr is null ? default : unsafe(MemoryMarshal.Cast<byte, float>(_planeCr.Span));

            public float[] Block { get; } = new float[64];

            public double[] Scratch { get; } = new double[64];

            public int[] Coefficients { get; } = new int[64];

            public void Dispose()
            {
                _row?.Dispose();
                _row = null;
                _planeY?.Dispose();
                _planeY = null;
                _planeCb?.Dispose();
                _planeCb = null;
                _planeCr?.Dispose();
                _planeCr = null;
            }
        }

        /// <summary>Gets where the coefficients of a block of an MCU go.</summary>
        private delegate Span<short> BlockDestination(TransformWorker worker, int row, int mcuX, int block);
    }
}
