using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG encoder registration: DCT-based JPEG of 8-bit or 12-bit samples, sequential (SOF0, SOF1, or SOF9 with
/// arithmetic coding) or progressive (SOF2, SOF10), grayscale or YCbCr, written by one streaming session; lossless JPEG
/// (SOF3, SOF11) is written by <see cref="JpegLosslessEncoderSession"/>.
/// </summary>
internal sealed class JpegEncoderCodec : ImageEncoderCodec
{
    /// <summary>The worst-case size of one entropy-coded block: DC (at most 11 + 11 bits) and 63 AC coefficients (at most 16 + 10 bits each), doubled for byte stuffing.</summary>
    internal const int MaxBlockBytes = 2 * ((22 + (63 * 26) + 7) / 8);

    /// <summary>The worst-case size of one entropy-coded block of 12-bit samples: DC (at most 16 + 15 bits) and 63 AC coefficients (at most 16 + 14 bits each), doubled for byte stuffing.</summary>
    internal const int MaxWideBlockBytes = 2 * ((31 + (63 * 30) + 7) / 8);

    public static JpegEncoderCodec Instance { get; } = new();

    /// <summary>
    /// For a value <c>v</c> from -2048 to 2047, at the index <c>v + 2048</c>: its size category SSSS (the number of bits of
    /// its magnitude, T.81 F.1.2.1.1) in the high 16 bits, and its SSSS additional bits (<c>v</c>, or <c>v - 1</c> for a
    /// negative value) in the low 16 bits.
    /// </summary>
    private static readonly uint[] ValueBits = CreateValueBits();

    public override ImageFormat Format => ImageFormat.Jpeg;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ((JpegEncoder)options.Encoder).Process == JpegEncodingProcess.Lossless ? new JpegLosslessEncoderSession(options) : new Session(options);
    }

    /// <summary>
    /// Gets the sample precision of an output, and checks that the pixels may be reduced to it: an explicit
    /// <see cref="JpegEncoder.BitDepth"/>, or <see cref="JpegEncoder.AllowBitDepthReduction"/> for the default precision.
    /// </summary>
    /// <param name="encoder">The settings.</param>
    /// <param name="pixelFormat">The pixel format of the frames.</param>
    /// <param name="options">The name of the parameter of the session options, for the exceptions.</param>
    /// <returns>The precision: 8 or 12 bits for the DCT processes, 2 to 16 bits for the lossless one.</returns>
    /// <exception cref="ArgumentException">The precision is not one of the process.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels are deeper than the default precision and no reduction is allowed.</exception>
    internal static int GetPrecision(JpegEncoder encoder, PixelFormat pixelFormat, string options)
    {
        var sourceBits = PixelFormats.GetBitsPerComponent(pixelFormat);
        var lossless = encoder.Process == JpegEncodingProcess.Lossless;
        if (encoder.BitDepth is { } bitDepth)
        {
            if (!lossless && bitDepth is not (8 or 12))
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A JPEG DCT process stores 8-bit or 12-bit samples, not {bitDepth}-bit ones: set JpegEncoder.BitDepth to 8 or 12, or use JpegEncodingProcess.Lossless."), options);

            return bitDepth;
        }

        var precision = lossless ? Math.Min(sourceBits, 16) : 8;
        if (sourceBits > precision && !encoder.AllowBitDepthReduction)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"JPEG output stores {precision}-bit samples, so encoding {pixelFormat} pixels would discard precision. Set JpegEncoder.AllowBitDepthReduction to reduce the samples to {precision} bits (nearest rounding), set JpegEncoder.BitDepth, or convert the image explicitly with CloneAs."), ImageFormat.Jpeg, "Bit depth reduction");

        return precision;
    }

    private static uint[] CreateValueBits()
    {
        var table = new uint[4096];
        for (var value = -2048; value < 2048; value++)
        {
            var size = 32 - BitOperations.LeadingZeroCount((uint)Math.Abs(value));
            table[value + 2048] = ((uint)size << 16) | ((uint)(value < 0 ? value - 1 : value) & ((1u << size) - 1));
        }

        return table;
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

    // The reciprocals of 1.772 and 1.402 (Cb and Cr scales of JFIF 1.02)
    private const double BlueScale = 1 / 1.772;
    private const double RedScale = 1 / 1.402;

    // The denominators of the exact fractions of the luma, Cb and Cr (see ConvertRgbToYCbCr)
    private const float LumaDenominator = 1000;
    private const float BlueDenominator = 1772;
    private const float RedDenominator = 1402;

    /// <summary>
    /// Converts 8-bit RGB pixels to level-shifted JFIF 1.02 full-range YCbCr samples (ITU-R BT.601 luma weights):
    /// <c>Y = 0.299 R + 0.587 G + 0.114 B</c>, <c>Cb = (B - Y) / 1.772</c>, <c>Cr = (R - Y) / 1.402</c>, computed in double
    /// precision and stored unrounded (single precision) after the level shift of 128 (<c>Y - 128</c>), so the only rounding
    /// before the DCT is the conversion to single precision. The divisions are multiplications by the double-precision
    /// reciprocals: for each of the 2^24 colors, the single-precision results are those of the divisions (checked
    /// exhaustively), and a multiplication is several times faster.
    /// </summary>
    /// <param name="source">The interleaved RGB samples, 3 bytes per pixel.</param>
    /// <param name="luma">The level-shifted luma samples; its length is the number of pixels.</param>
    /// <param name="blue">The Cb samples minus 128.</param>
    /// <param name="red">The Cr samples minus 128.</param>
    /// <remarks>
    /// <para>
    /// With 128-bit hardware vectors, 16 pixels are deinterleaved by byte shuffles (one LD3 load on Arm64) and converted two
    /// at a time (from exact single-precision conversions of the samples on Arm64) with the same
    /// operations in the same order (separate multiplications and additions), so the samples are identical;
    /// with AVX2, four at a time, and with AVX-512, eight at a time.
    /// </para>
    /// <para>
    /// With Arm64, AVX2 and AVX-512, the 16-pixel groups are converted faster from the exact values, which are fractions with
    /// integer numerators: <c>Y - 128 = (t - 128000) / 1000</c> with <c>t = 299 R + 587 G + 114 B</c>,
    /// <c>Cb = (1000 B - t) / 1772</c> and <c>Cr = (1000 R - t) / 1402</c>. The numerators are exact in single precision
    /// (their magnitudes are below 2^24) and a single-precision division is correctly rounded, so each sample is its exact
    /// value rounded once, four (Arm64), eight (AVX2) or sixteen (AVX-512) at a time. For each of the 2^24 colors this is
    /// the single-precision result of the double-precision formula (checked exhaustively), except where a numerator is zero
    /// and the double-precision luma has a rounding error that the formula keeps: some colors whose luma is exactly 128
    /// (<c>t = 128000</c>), and some gray levels (<c>R = G = B</c>, whose Cb and Cr numerators are zero). The groups with such
    /// a pixel, a luma of 128 or a gray pixel, are converted by the double-precision path.
    /// </para>
    /// </remarks>
    internal static void ConvertRgbToYCbCr(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red) => ConvertRgbToYCbCr<JpegRgbLayout>(source, luma, blue, red);

    /// <summary>Converts 8-bit pixels of the layout <typeparamref name="TLayout"/> like <see cref="ConvertRgbToYCbCr(ReadOnlySpan{byte}, Span{float}, Span{float}, Span{float})"/> (the alpha samples of a 4-byte layout are ignored).</summary>
    internal static void ConvertRgbToYCbCr<TLayout>(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        where TLayout : struct, IJpegSourceLayout
    {
        var done = Vector512.IsHardwareAccelerated && Avx512F.IsSupported ? ConvertRgbToYCbCrRationalAvx512<TLayout>(source, luma, blue, red)
            : Avx2.IsSupported ? ConvertRgbToYCbCrRationalAvx2<TLayout>(source, luma, blue, red)
            : AdvSimd.Arm64.IsSupported ? ConvertRgbToYCbCrRationalArm64<TLayout>(source, luma, blue, red)
            : Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian ? ConvertRgbToYCbCrVector<TLayout>(source, luma, blue, red)
            : 0;
        ConvertRgbToYCbCrScalar<TLayout>(source[(TLayout.PixelSize * done)..], luma[done..], blue[done..], red[done..]);
    }

    /// <summary>
    /// Converts the pixels of whole 16-pixel groups with Arm64 from the exact fractions (see <see cref="ConvertRgbToYCbCr"/>):
    /// the numerators from widening multiply-adds of the 16-bit samples into 32-bit integers, then single-precision
    /// divisions; the groups with a luma of 128 or a gray pixel are converted by <see cref="ConvertRgbToYCbCrVector"/>.
    /// </summary>
    /// <returns>The number of pixels converted.</returns>
    internal static int ConvertRgbToYCbCrRationalArm64(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red) => ConvertRgbToYCbCrRationalArm64<JpegRgbLayout>(source, luma, blue, red);

    internal static int ConvertRgbToYCbCrRationalArm64<TLayout>(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        where TLayout : struct, IJpegSourceLayout
    {
        var count = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), source.Length / TLayout.PixelSize);
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        var x = 0;
        unsafe
        {
            fixed (byte* pixels = source)
            {
                for (; x + 16 <= count; x += 16)
                {
                    var (r, g, b) = LoadArm64<TLayout>(pixels + (TLayout.PixelSize * x));
                    ref var lumaGroup = ref Unsafe.Add(ref lumaStart, x);
                    ref var blueGroup = ref Unsafe.Add(ref blueStart, x);
                    ref var redGroup = ref Unsafe.Add(ref redStart, x);
                    if ((Vector128.Equals(r, g) & Vector128.Equals(g, b)) != Vector128<byte>.Zero)
                    {
                        ConvertGroupVector(r, g, b, ref lumaGroup, ref blueGroup, ref redGroup);
                        continue;
                    }

                    var r0 = AdvSimd.ZeroExtendWideningLower(r.GetLower());
                    var r1 = AdvSimd.ZeroExtendWideningUpper(r);
                    var g0 = AdvSimd.ZeroExtendWideningLower(g.GetLower());
                    var g1 = AdvSimd.ZeroExtendWideningUpper(g);
                    var b0 = AdvSimd.ZeroExtendWideningLower(b.GetLower());
                    var b1 = AdvSimd.ZeroExtendWideningUpper(b);

                    // t = 299 R + 587 G + 114 B for the pixels 0-3, 4-7, 8-11 and 12-15
                    var t0 = AdvSimd.MultiplyWideningLowerAndAdd(AdvSimd.MultiplyWideningLowerAndAdd(AdvSimd.MultiplyWideningLower(r0.GetLower(), Vector64.Create((ushort)299)), g0.GetLower(), Vector64.Create((ushort)587)), b0.GetLower(), Vector64.Create((ushort)114)).AsInt32();
                    var t1 = AdvSimd.MultiplyWideningUpperAndAdd(AdvSimd.MultiplyWideningUpperAndAdd(AdvSimd.MultiplyWideningUpper(r0, Vector128.Create((ushort)299)), g0, Vector128.Create((ushort)587)), b0, Vector128.Create((ushort)114)).AsInt32();
                    var t2 = AdvSimd.MultiplyWideningLowerAndAdd(AdvSimd.MultiplyWideningLowerAndAdd(AdvSimd.MultiplyWideningLower(r1.GetLower(), Vector64.Create((ushort)299)), g1.GetLower(), Vector64.Create((ushort)587)), b1.GetLower(), Vector64.Create((ushort)114)).AsInt32();
                    var t3 = AdvSimd.MultiplyWideningUpperAndAdd(AdvSimd.MultiplyWideningUpperAndAdd(AdvSimd.MultiplyWideningUpper(r1, Vector128.Create((ushort)299)), g1, Vector128.Create((ushort)587)), b1, Vector128.Create((ushort)114)).AsInt32();
                    var y0 = t0 - Vector128.Create(128000);
                    var y1 = t1 - Vector128.Create(128000);
                    var y2 = t2 - Vector128.Create(128000);
                    var y3 = t3 - Vector128.Create(128000);
                    if ((Vector128.Equals(y0, Vector128<int>.Zero) | Vector128.Equals(y1, Vector128<int>.Zero) | Vector128.Equals(y2, Vector128<int>.Zero) | Vector128.Equals(y3, Vector128<int>.Zero)) != Vector128<int>.Zero)
                    {
                        ConvertGroupVector(r, g, b, ref lumaGroup, ref blueGroup, ref redGroup);
                        continue;
                    }

                    Store(y0, LumaDenominator, ref lumaGroup, 0);
                    Store(y1, LumaDenominator, ref lumaGroup, 4);
                    Store(y2, LumaDenominator, ref lumaGroup, 8);
                    Store(y3, LumaDenominator, ref lumaGroup, 12);
                    Store(AdvSimd.MultiplyWideningLower(b0.GetLower(), Vector64.Create((ushort)1000)).AsInt32() - t0, BlueDenominator, ref blueGroup, 0);
                    Store(AdvSimd.MultiplyWideningUpper(b0, Vector128.Create((ushort)1000)).AsInt32() - t1, BlueDenominator, ref blueGroup, 4);
                    Store(AdvSimd.MultiplyWideningLower(b1.GetLower(), Vector64.Create((ushort)1000)).AsInt32() - t2, BlueDenominator, ref blueGroup, 8);
                    Store(AdvSimd.MultiplyWideningUpper(b1, Vector128.Create((ushort)1000)).AsInt32() - t3, BlueDenominator, ref blueGroup, 12);
                    Store(AdvSimd.MultiplyWideningLower(r0.GetLower(), Vector64.Create((ushort)1000)).AsInt32() - t0, RedDenominator, ref redGroup, 0);
                    Store(AdvSimd.MultiplyWideningUpper(r0, Vector128.Create((ushort)1000)).AsInt32() - t1, RedDenominator, ref redGroup, 4);
                    Store(AdvSimd.MultiplyWideningLower(r1.GetLower(), Vector64.Create((ushort)1000)).AsInt32() - t2, RedDenominator, ref redGroup, 8);
                    Store(AdvSimd.MultiplyWideningUpper(r1, Vector128.Create((ushort)1000)).AsInt32() - t3, RedDenominator, ref redGroup, 12);
                }
            }
        }

        return x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Store(Vector128<int> numerator, float denominator, ref float destination, int index)
        {
            unsafe { (Vector128.ConvertToSingle(numerator) / Vector128.Create(denominator)).StoreUnsafe(ref destination, (nuint)index); }
        }
    }

    /// <summary>
    /// Converts the pixels of whole 16-pixel groups with AVX2 from the exact fractions (see <see cref="ConvertRgbToYCbCr"/>),
    /// eight at a time: the numerators are computed in single precision, where they are exact integers; the groups with a
    /// luma of 128 or a gray pixel are converted by <see cref="ConvertRgbToYCbCrAvx2"/>.
    /// </summary>
    /// <returns>The number of pixels converted.</returns>
    internal static int ConvertRgbToYCbCrRationalAvx2(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red) => ConvertRgbToYCbCrRationalAvx2<JpegRgbLayout>(source, luma, blue, red);

    internal static int ConvertRgbToYCbCrRationalAvx2<TLayout>(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        where TLayout : struct, IJpegSourceLayout
    {
        var count = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), source.Length / TLayout.PixelSize);
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        var x = 0;
        for (; x + 16 <= count; x += 16)
        {
            var (r, g, b) = Deinterleave<TLayout>(ref unsafe(Unsafe.Add(ref sourceStart, TLayout.PixelSize * x)));
            ref var lumaGroup = ref unsafe(Unsafe.Add(ref lumaStart, x));
            ref var blueGroup = ref unsafe(Unsafe.Add(ref blueStart, x));
            ref var redGroup = ref unsafe(Unsafe.Add(ref redStart, x));
            if ((Vector128.Equals(r, g) & Vector128.Equals(g, b)) != Vector128<byte>.Zero)
            {
                ConvertGroupAvx2(r, g, b, ref lumaGroup, ref blueGroup, ref redGroup);
                continue;
            }

            var r0 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(r));
            var g0 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(g));
            var b0 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(b));
            var r1 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(r, 8)));
            var g1 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(g, 8)));
            var b1 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(b, 8)));
            var t0 = (r0 * 299f) + (g0 * 587f) + (b0 * 114f);
            var t1 = (r1 * 299f) + (g1 * 587f) + (b1 * 114f);
            var y0 = t0 - Vector256.Create(128000f);
            var y1 = t1 - Vector256.Create(128000f);
            if ((Vector256.Equals(y0, Vector256<float>.Zero) | Vector256.Equals(y1, Vector256<float>.Zero)) != Vector256<float>.Zero)
            {
                ConvertGroupAvx2(r, g, b, ref lumaGroup, ref blueGroup, ref redGroup);
                continue;
            }

            unsafe { (y0 / Vector256.Create(LumaDenominator)).StoreUnsafe(ref lumaGroup); }
            unsafe { (y1 / Vector256.Create(LumaDenominator)).StoreUnsafe(ref lumaGroup, 8); }
            unsafe { (((b0 * 1000f) - t0) / Vector256.Create(BlueDenominator)).StoreUnsafe(ref blueGroup); }
            unsafe { (((b1 * 1000f) - t1) / Vector256.Create(BlueDenominator)).StoreUnsafe(ref blueGroup, 8); }
            unsafe { (((r0 * 1000f) - t0) / Vector256.Create(RedDenominator)).StoreUnsafe(ref redGroup); }
            unsafe { (((r1 * 1000f) - t1) / Vector256.Create(RedDenominator)).StoreUnsafe(ref redGroup, 8); }
        }

        return x;
    }

    /// <summary>
    /// Converts the pixels of whole 16-pixel groups with AVX-512 from the exact fractions (see <see cref="ConvertRgbToYCbCr"/>),
    /// sixteen at a time: the samples are deinterleaved by byte permutations of the 48 bytes with AVX-512 VBMI (while 64 bytes
    /// can be loaded), or by byte shuffles; the groups with a luma of 128 or a gray pixel are converted by
    /// <see cref="ConvertRgbToYCbCrAvx512"/>.
    /// </summary>
    /// <returns>The number of pixels converted.</returns>
    internal static int ConvertRgbToYCbCrRationalAvx512(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red) => ConvertRgbToYCbCrRationalAvx512<JpegRgbLayout>(source, luma, blue, red);

    internal static int ConvertRgbToYCbCrRationalAvx512<TLayout>(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        where TLayout : struct, IJpegSourceLayout
    {
        var count = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), source.Length / TLayout.PixelSize);
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        var x = 0;
        for (; x + 16 <= count; x += 16)
        {
            Vector512<int> r;
            Vector512<int> g;
            Vector512<int> b;
            if (TLayout.PixelSize == 4)
            {
                // 16 pixels of 4 bytes: each sample is a byte of the 32-bit lanes
                var pixels = unsafe(Vector512.LoadUnsafe(ref sourceStart, (nuint)(4 * x))).AsInt32();
                var low = Vector512.Create(0xFF);
                r = pixels & low;
                g = (pixels >>> 8) & low;
                b = (pixels >>> 16) & low;
            }
            else if (Avx512Vbmi.IsSupported && (3 * x) + 64 <= source.Length)
            {
                // The byte 3 k + c of the pixels in the low byte of the 32-bit lane k
                var pixels = unsafe(Vector512.LoadUnsafe(ref sourceStart, (nuint)(3 * x)));
                var low = Vector512.Create(0xFF);
                r = Avx512Vbmi.PermuteVar64x8(pixels, Vector512.Create(0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 39, 42, 45).AsByte()).AsInt32() & low;
                g = Avx512Vbmi.PermuteVar64x8(pixels, Vector512.Create(1, 4, 7, 10, 13, 16, 19, 22, 25, 28, 31, 34, 37, 40, 43, 46).AsByte()).AsInt32() & low;
                b = Avx512Vbmi.PermuteVar64x8(pixels, Vector512.Create(2, 5, 8, 11, 14, 17, 20, 23, 26, 29, 32, 35, 38, 41, 44, 47).AsByte()).AsInt32() & low;
            }
            else
            {
                var (r8, g8, b8) = Deinterleave(ref unsafe(Unsafe.Add(ref sourceStart, 3 * x)));
                r = Avx512F.ConvertToVector512Int32(r8);
                g = Avx512F.ConvertToVector512Int32(g8);
                b = Avx512F.ConvertToVector512Int32(b8);
            }

            if (TLayout.IsBgr)
            {
                (r, b) = (b, r);
            }

            var rs = Avx512F.ConvertToVector512Single(r);
            var gs = Avx512F.ConvertToVector512Single(g);
            var bs = Avx512F.ConvertToVector512Single(b);
            var t = (rs * 299f) + (gs * 587f) + (bs * 114f);
            var y = t - Vector512.Create(128000f);
            if (Vector512.EqualsAny(y, Vector512<float>.Zero) || (Vector512.Equals(r, g) & Vector512.Equals(g, b)) != Vector512<int>.Zero)
            {
                ConvertGroupAvx512(r, g, b, ref unsafe(Unsafe.Add(ref lumaStart, x)), ref unsafe(Unsafe.Add(ref blueStart, x)), ref unsafe(Unsafe.Add(ref redStart, x)));
                continue;
            }

            unsafe { (y / Vector512.Create(LumaDenominator)).StoreUnsafe(ref lumaStart, (nuint)x); }
            unsafe { (((bs * 1000f) - t) / Vector512.Create(BlueDenominator)).StoreUnsafe(ref blueStart, (nuint)x); }
            unsafe { (((rs * 1000f) - t) / Vector512.Create(RedDenominator)).StoreUnsafe(ref redStart, (nuint)x); }
        }

        return x;
    }

    /// <summary>The scalar reference of <see cref="ConvertRgbToYCbCr"/>.</summary>
    internal static void ConvertRgbToYCbCrScalar(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red) => ConvertRgbToYCbCrScalar<JpegRgbLayout>(source, luma, blue, red);

    /// <summary>The scalar reference of <see cref="ConvertRgbToYCbCr{TLayout}"/>.</summary>
    internal static void ConvertRgbToYCbCrScalar<TLayout>(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        where TLayout : struct, IJpegSourceLayout
    {
        for (var x = 0; x < luma.Length; x++)
        {
            var pixel = source.Slice(TLayout.PixelSize * x, 3);
            double r = pixel[TLayout.IsBgr ? 2 : 0];
            double g = pixel[1];
            double b = pixel[TLayout.IsBgr ? 0 : 2];
            var y = (0.299 * r) + (0.587 * g) + (0.114 * b);
            luma[x] = (float)(y - 128);
            blue[x] = (float)((b - y) * BlueScale);
            red[x] = (float)((r - y) * RedScale);
        }
    }

    /// <summary>Converts the pixels of whole 16-pixel groups.</summary>
    /// <returns>The number of pixels converted.</returns>
    internal static int ConvertRgbToYCbCrVector(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red) => ConvertRgbToYCbCrVector<JpegRgbLayout>(source, luma, blue, red);

    internal static int ConvertRgbToYCbCrVector<TLayout>(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        where TLayout : struct, IJpegSourceLayout
    {
        var count = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), source.Length / TLayout.PixelSize);
        ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        var x = 0;
        if (AdvSimd.Arm64.IsSupported)
        {
            // LD3 (LD4) loads and deinterleaves 16 pixels
            unsafe
            {
                fixed (byte* pixels = source)
                {
                    for (; x + 16 <= count; x += 16)
                    {
                        var (r, g, b) = LoadArm64<TLayout>(pixels + (TLayout.PixelSize * x));
                        ConvertGroupVector(r, g, b, ref Unsafe.Add(ref lumaStart, x), ref Unsafe.Add(ref blueStart, x), ref Unsafe.Add(ref redStart, x));
                    }
                }
            }

            return x;
        }

        for (; x + 16 <= count; x += 16)
        {
            var (r, g, b) = Deinterleave<TLayout>(ref unsafe(Unsafe.Add(ref sourceStart, TLayout.PixelSize * x)));
            ConvertGroupVector(r, g, b, ref unsafe(Unsafe.Add(ref lumaStart, x)), ref unsafe(Unsafe.Add(ref blueStart, x)), ref unsafe(Unsafe.Add(ref redStart, x)));
        }

        return x;
    }

    /// <summary>Converts 16 deinterleaved pixels like <see cref="ConvertRgbToYCbCrVector"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertGroupVector(Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, ref float luma, ref float blue, ref float red)
    {
        var (r0, r1) = Vector128.Widen(r);
        var (g0, g1) = Vector128.Widen(g);
        var (b0, b1) = Vector128.Widen(b);
        Convert8(r0, g0, b0, ref luma, ref blue, ref red);
        Convert8(r1, g1, b1, ref unsafe(Unsafe.Add(ref luma, 8)), ref unsafe(Unsafe.Add(ref blue, 8)), ref unsafe(Unsafe.Add(ref red, 8)));

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

            // The samples (below 2^24) are converted exactly to single precision, then widened exactly to double precision
            var rs = Vector128.ConvertToSingle(r);
            var gs = Vector128.ConvertToSingle(g);
            var bs = Vector128.ConvertToSingle(b);
            Convert2(Vector128.WidenLower(rs), Vector128.WidenLower(gs), Vector128.WidenLower(bs), out var y0, out var cb0, out var cr0);
            Convert2(Vector128.WidenUpper(rs), Vector128.WidenUpper(gs), Vector128.WidenUpper(bs), out var y1, out var cb1, out var cr1);
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
            blue = (b - y) * Vector128.Create(BlueScale);
            red = (r - y) * Vector128.Create(RedScale);
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
            ConvertGroupAvx2(r, g, b, ref unsafe(Unsafe.Add(ref lumaStart, x)), ref unsafe(Unsafe.Add(ref blueStart, x)), ref unsafe(Unsafe.Add(ref redStart, x)));
        }

        return x;
    }

    /// <summary>Converts 16 deinterleaved pixels like <see cref="ConvertRgbToYCbCrAvx2"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertGroupAvx2(Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, ref float luma, ref float blue, ref float red)
    {
        Convert8(Avx2.ConvertToVector256Int32(r), Avx2.ConvertToVector256Int32(g), Avx2.ConvertToVector256Int32(b), ref luma, ref blue, ref red);
        Convert8(
            Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(r, 8)),
            Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(g, 8)),
            Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(b, 8)),
            ref unsafe(Unsafe.Add(ref luma, 8)), ref unsafe(Unsafe.Add(ref blue, 8)), ref unsafe(Unsafe.Add(ref red, 8)));

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
            unsafe { Avx.ConvertToVector128Single((b - y) * Vector256.Create(BlueScale)).StoreUnsafe(ref blue); }
            unsafe { Avx.ConvertToVector128Single((r - y) * Vector256.Create(RedScale)).StoreUnsafe(ref red); }
        }
    }

    /// <summary>Converts the pixels of whole 16-pixel groups with AVX-512, eight at a time.</summary>
    /// <returns>The number of pixels converted.</returns>
    internal static int ConvertRgbToYCbCrAvx512(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
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
            ConvertGroupAvx512(Avx512F.ConvertToVector512Int32(r), Avx512F.ConvertToVector512Int32(g), Avx512F.ConvertToVector512Int32(b), ref unsafe(Unsafe.Add(ref lumaStart, x)), ref unsafe(Unsafe.Add(ref blueStart, x)), ref unsafe(Unsafe.Add(ref redStart, x)));
        }

        return x;
    }

    /// <summary>Converts 16 pixels (their samples in 32-bit lanes) like <see cref="ConvertRgbToYCbCrAvx512"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertGroupAvx512(Vector512<int> r32, Vector512<int> g32, Vector512<int> b32, ref float luma, ref float blue, ref float red)
    {
        Convert8(r32.GetLower(), g32.GetLower(), b32.GetLower(), ref luma, ref blue, ref red);
        Convert8(r32.GetUpper(), g32.GetUpper(), b32.GetUpper(), ref unsafe(Unsafe.Add(ref luma, 8)), ref unsafe(Unsafe.Add(ref blue, 8)), ref unsafe(Unsafe.Add(ref red, 8)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Convert8(Vector256<int> red8, Vector256<int> green8, Vector256<int> blue8, ref float luma, ref float blue, ref float red)
        {
            var r = Avx512F.ConvertToVector512Double(red8);
            var g = Avx512F.ConvertToVector512Double(green8);
            var b = Avx512F.ConvertToVector512Double(blue8);
            var y = (0.299 * r) + (0.587 * g) + (0.114 * b);
            unsafe { Avx512F.ConvertToVector256Single(y - Vector512.Create(128.0)).StoreUnsafe(ref luma); }
            unsafe { Avx512F.ConvertToVector256Single((b - y) * Vector512.Create(BlueScale)).StoreUnsafe(ref blue); }
            unsafe { Avx512F.ConvertToVector256Single((r - y) * Vector512.Create(RedScale)).StoreUnsafe(ref red); }
        }
    }

    /// <summary>Loads and deinterleaves 16 pixels of the layout <typeparamref name="TLayout"/> with Arm64 (LD3 or LD4).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<byte> R, Vector128<byte> G, Vector128<byte> B) LoadArm64<TLayout>(byte* pixels)
        where TLayout : struct, IJpegSourceLayout
    {
        Vector128<byte> first;
        Vector128<byte> green;
        Vector128<byte> third;
        if (TLayout.PixelSize == 4)
        {
            (first, green, third, _) = unsafe(AdvSimd.Arm64.Load4xVector128AndUnzip(pixels));
        }
        else
        {
            (first, green, third) = unsafe(AdvSimd.Arm64.Load3xVector128AndUnzip(pixels));
        }

        return TLayout.IsBgr ? (third, green, first) : (first, green, third);
    }

    /// <summary>Deinterleaves 16 pixels of the layout <typeparamref name="TLayout"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<byte> R, Vector128<byte> G, Vector128<byte> B) Deinterleave<TLayout>(ref byte source)
        where TLayout : struct, IJpegSourceLayout
    {
        var (first, green, third) = TLayout.PixelSize == 4 ? Deinterleave4(ref source) : Deinterleave(ref source);
        return TLayout.IsBgr ? (third, green, first) : (first, green, third);
    }

    /// <summary>
    /// Deinterleaves the first three samples of 16 pixels of 4 bytes: each vector of 4 pixels is shuffled into its four
    /// samples of each channel (one 32-bit lane per channel), then the lanes are gathered by interleaving.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<byte> First, Vector128<byte> Second, Vector128<byte> Third) Deinterleave4(ref byte source)
    {
        var order = Vector128.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15);
        var v0 = Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref source)), order).AsUInt32();
        var v1 = Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref source, 16)), order).AsUInt32();
        var v2 = Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref source, 32)), order).AsUInt32();
        var v3 = Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref source, 48)), order).AsUInt32();
        if (Sse2.IsSupported)
        {
            var low01 = Sse2.UnpackLow(v0, v1).AsUInt64();
            var low23 = Sse2.UnpackLow(v2, v3).AsUInt64();
            var high01 = Sse2.UnpackHigh(v0, v1).AsUInt64();
            var high23 = Sse2.UnpackHigh(v2, v3).AsUInt64();
            return (Sse2.UnpackLow(low01, low23).AsByte(), Sse2.UnpackHigh(low01, low23).AsByte(), Sse2.UnpackLow(high01, high23).AsByte());
        }

        return (
            Vector128.Create(v0.GetElement(0), v1.GetElement(0), v2.GetElement(0), v3.GetElement(0)).AsByte(),
            Vector128.Create(v0.GetElement(1), v1.GetElement(1), v2.GetElement(1), v3.GetElement(1)).AsByte(),
            Vector128.Create(v0.GetElement(2), v1.GetElement(2), v2.GetElement(2), v3.GetElement(2)).AsByte());
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

    /// <summary>Stores 64 quantized coefficients (within the 16-bit range) as 16-bit values.</summary>
    internal static void NarrowCoefficients(ReadOnlySpan<int> coefficients, Span<short> destination)
    {
        coefficients = coefficients[..64];
        destination = destination[..64];
        if (!Vector128.IsHardwareAccelerated)
        {
            for (var k = 0; k < 64; k++)
            {
                destination[k] = (short)coefficients[k];
            }

            return;
        }

        ref var source = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        for (var k = 0; k < 64; k += 8)
        {
            var low = unsafe(Vector128.LoadUnsafe(ref source, (nuint)k));
            var high = unsafe(Vector128.LoadUnsafe(ref source, (nuint)(k + 4)));
            unsafe { Vector128.Narrow(low, high).StoreUnsafe(ref target, (nuint)k); }
        }
    }

    /// <summary>Gets a mask of the non-zero values among 64 coefficients: bit <c>k</c> is set when <c>coefficients[k]</c> is not zero.</summary>
    /// <param name="coefficients">The 64 coefficients.</param>
    internal static ulong GetNonZeroMask(ReadOnlySpan<int> coefficients)
    {
        coefficients = coefficients[..64];
        if (!Vector128.IsHardwareAccelerated)
            return GetNonZeroMaskScalar(coefficients);

        ref var start = ref unsafe(MemoryMarshal.GetReference(coefficients));
        var zeros = 0UL;
        if (Vector512.IsHardwareAccelerated)
        {
            // One mask bit per comparison of 16 coefficients
            for (var i = 0; i < 64; i += 16)
            {
                zeros |= (ulong)Vector512.Equals(unsafe(Vector512.LoadUnsafe(ref start, (nuint)i)), Vector512<int>.Zero).ExtractMostSignificantBits() << i;
            }

            return ~zeros;
        }

        if (AdvSimd.Arm64.IsSupported)
        {
            // The comparison results narrowed to 64 bytes, each one weighted by its bit (1 to 128) and summed by groups of 8
            // with three pairwise additions: the 8 bytes of the mask
            var weights = Vector128.Create((byte)1, 2, 4, 8, 16, 32, 64, 128, 1, 2, 4, 8, 16, 32, 64, 128);
            var low = AdvSimd.Arm64.AddPairwise(EqualsZero(ref start, 0) & weights, EqualsZero(ref start, 16) & weights);
            var high = AdvSimd.Arm64.AddPairwise(EqualsZero(ref start, 32) & weights, EqualsZero(ref start, 48) & weights);
            var quarters = AdvSimd.Arm64.AddPairwise(low, high);
            return ~AdvSimd.Arm64.AddPairwise(quarters, quarters).AsUInt64().ToScalar();
        }

        // The comparison results (all ones or zero) of 16 coefficients narrowed to 16 bytes, one mask bit each
        for (var i = 0; i < 64; i += 16)
        {
            zeros |= (ulong)EqualsZero(ref start, i).ExtractMostSignificantBits() << i;
        }

        return ~zeros;

        // The comparison results (all ones or zero) of 16 coefficients narrowed to 16 bytes
        static Vector128<byte> EqualsZero(ref int start, int index)
        {
            var first = Vector128.Narrow(Vector128.Equals(Load(ref start, index), Vector128<int>.Zero), Vector128.Equals(Load(ref start, index + 4), Vector128<int>.Zero));
            var second = Vector128.Narrow(Vector128.Equals(Load(ref start, index + 8), Vector128<int>.Zero), Vector128.Equals(Load(ref start, index + 12), Vector128<int>.Zero));
            return Vector128.Narrow(first, second).AsByte();
        }

        static Vector128<int> Load(ref int start, int index) => unsafe(Vector128.LoadUnsafe(ref start, (nuint)index));
    }

    /// <summary>Gets a mask of the non-zero values among 64 stored (16-bit) coefficients: bit <c>k</c> is set when <c>coefficients[k]</c> is not zero.</summary>
    /// <param name="coefficients">The 64 coefficients.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong GetNonZeroMask(ReadOnlySpan<short> coefficients)
    {
        coefficients = coefficients[..64];
        if (!Vector128.IsHardwareAccelerated)
            return GetNonZeroMaskScalar(coefficients);

        ref var start = ref unsafe(MemoryMarshal.GetReference(coefficients));
        if (Vector512.IsHardwareAccelerated)
        {
            // One mask bit per comparison of 32 coefficients
            var lower = Vector512.Equals(unsafe(Vector512.LoadUnsafe(ref start)), Vector512<short>.Zero).ExtractMostSignificantBits();
            var upper = Vector512.Equals(unsafe(Vector512.LoadUnsafe(ref start, 32)), Vector512<short>.Zero).ExtractMostSignificantBits();
            return ~(lower | (upper << 32));
        }

        if (AdvSimd.Arm64.IsSupported)
        {
            // As for 32-bit coefficients: the comparison results weighted by their bit and summed by groups of 8
            var weights = Vector128.Create((byte)1, 2, 4, 8, 16, 32, 64, 128, 1, 2, 4, 8, 16, 32, 64, 128);
            var low = AdvSimd.Arm64.AddPairwise(EqualsZero(ref start, 0) & weights, EqualsZero(ref start, 16) & weights);
            var high = AdvSimd.Arm64.AddPairwise(EqualsZero(ref start, 32) & weights, EqualsZero(ref start, 48) & weights);
            var quarters = AdvSimd.Arm64.AddPairwise(low, high);
            return ~AdvSimd.Arm64.AddPairwise(quarters, quarters).AsUInt64().ToScalar();
        }

        var zeros = 0UL;
        for (var i = 0; i < 64; i += 16)
        {
            zeros |= (ulong)EqualsZero(ref start, i).ExtractMostSignificantBits() << i;
        }

        return ~zeros;

        // The comparison results (all ones or zero) of 16 coefficients narrowed to 16 bytes
        static Vector128<byte> EqualsZero(ref short start, int index) =>
            Vector128.Narrow(Vector128.Equals(Load(ref start, index), Vector128<short>.Zero), Vector128.Equals(Load(ref start, index + 8), Vector128<short>.Zero)).AsByte();

        static Vector128<short> Load(ref short start, int index) => unsafe(Vector128.LoadUnsafe(ref start, (nuint)index));
    }

    /// <summary>The scalar reference of <see cref="GetNonZeroMask(ReadOnlySpan{short})"/>.</summary>
    internal static ulong GetNonZeroMaskScalar(ReadOnlySpan<short> coefficients)
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

    /// <summary>The scalar reference of <see cref="GetNonZeroMask(ReadOnlySpan{int})"/>.</summary>
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

    /// <summary>Copies the 8x8 block of samples that starts at <c>plane[0]</c>, whose rows are <paramref name="stride"/> samples apart.</summary>
    /// <param name="plane">The samples, from the first one of the block.</param>
    /// <param name="stride">The distance between two rows of the plane.</param>
    /// <param name="block">The 64 samples of the block.</param>
    internal static void CopyBlock(ReadOnlySpan<float> plane, int stride, Span<float> block)
    {
        // Bounds are checked once here; the loop below uses unchecked references
        _ = plane[..((7 * stride) + 8)];
        _ = block[..64];
        ref var source = ref unsafe(MemoryMarshal.GetReference(plane));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(block));
        for (var y = 0; y < 8; y++)
        {
            ref var row = ref unsafe(Unsafe.Add(ref source, y * stride));
            if (Vector256.IsHardwareAccelerated)
            {
                unsafe { Vector256.LoadUnsafe(ref row).StoreUnsafe(ref destination, (nuint)(y * 8)); }
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                unsafe { Vector128.LoadUnsafe(ref row).StoreUnsafe(ref destination, (nuint)(y * 8)); }
                unsafe { Vector128.LoadUnsafe(ref row, 4).StoreUnsafe(ref destination, (nuint)((y * 8) + 4)); }
            }
            else
            {
                plane.Slice(y * stride, 8).CopyTo(block.Slice(y * 8, 8));
            }
        }
    }

    /// <summary>
    /// Subsamples a row of chroma samples horizontally by 2, and vertically by 2 when <paramref name="second"/> is not empty:
    /// each sample is the mean of the 2 or 4 full-resolution samples it covers (box filter: the subsampled sample sits at
    /// the center of those samples, the JFIF chroma siting), summed in double precision from zero, row by row, then
    /// multiplied by <c>1 / 2</c> or <c>1 / 4</c>.
    /// </summary>
    /// <param name="first">The first full-resolution row, two samples per subsampled sample.</param>
    /// <param name="second">The second full-resolution row (as long as <paramref name="first"/>), or an empty span for a vertical factor of 1.</param>
    /// <param name="destination">The subsampled samples.</param>
    /// <remarks>
    /// With hardware vectors, two (128-bit), four (AVX2) or eight (AVX-512) samples are computed at once with the same
    /// additions in the same order: the even and odd samples of a row come from interleaving operations, and with AVX2 and
    /// AVX-512 a permutation restores the order of the lanes at the end.
    /// </remarks>
    internal static void DownsampleRow(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> destination)
    {
        var done = Vector512.IsHardwareAccelerated && Avx512F.IsSupported ? DownsampleRowAvx512(first, second, destination)
            : Avx2.IsSupported ? DownsampleRowAvx2(first, second, destination)
            : AdvSimd.Arm64.IsSupported ? DownsampleRowArm64(first, second, destination)
            : Vector128.IsHardwareAccelerated ? DownsampleRowVector(first, second, destination)
            : 0;
        DownsampleRowScalar(first[(2 * done)..], second.IsEmpty ? second : second[(2 * done)..], destination[done..]);
    }

    /// <summary>The scalar reference of <see cref="DownsampleRow"/>.</summary>
    internal static void DownsampleRowScalar(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> destination)
    {
        var scale = second.IsEmpty ? 1d / 2 : 1d / 4;
        for (var x = 0; x < destination.Length; x++)
        {
            var sum = 0d;
            sum += first[2 * x];
            sum += first[(2 * x) + 1];
            if (!second.IsEmpty)
            {
                sum += second[2 * x];
                sum += second[(2 * x) + 1];
            }

            destination[x] = (float)(sum * scale);
        }
    }

    /// <summary>Subsamples the whole pairs of samples with 128-bit vectors, two at a time.</summary>
    /// <returns>The number of subsampled samples.</returns>
    internal static int DownsampleRowVector(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> destination)
    {
        var twoRows = !second.IsEmpty;
        var count = GetDownsampledCount(first, second, destination) & ~3;
        ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
        ref var secondStart = ref unsafe(MemoryMarshal.GetReference(twoRows ? second : first));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var scale = Vector128.Create(twoRows ? 1d / 4 : 1d / 2);
        for (var x = 0; x < count; x += 4)
        {
            // Samples x to x + 3 cover the samples 2x to 2x + 7: two at a time, (2x, 2x + 2) then (2x + 1, 2x + 3)
            var low = Mean(ref firstStart, ref secondStart, 2 * x, twoRows, scale);
            var high = Mean(ref firstStart, ref secondStart, (2 * x) + 4, twoRows, scale);
            unsafe { Vector128.Narrow(low, high).StoreUnsafe(ref output, (nuint)x); }
        }

        return count;

        // The mean of the samples (2i, 2i + 1) of one or two rows, for the samples i and i + 1 (i = start / 2)
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
    /// Subsamples the whole groups of four samples with Arm64: the pairs of the first row are added by FADDP (the sum of
    /// adjacent lanes, then added to zero as in the scalar reference), and the even and odd samples of the second row,
    /// separated by unzips (UZP1, UZP2) before they are widened, are added to them.
    /// </summary>
    /// <returns>The number of subsampled samples.</returns>
    internal static int DownsampleRowArm64(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> destination)
    {
        var twoRows = !second.IsEmpty;
        var count = GetDownsampledCount(first, second, destination) & ~3;
        ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
        ref var secondStart = ref unsafe(MemoryMarshal.GetReference(twoRows ? second : first));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var scale = Vector128.Create(twoRows ? 1d / 4 : 1d / 2);
        for (var x = 0; x < count; x += 4)
        {
            // Samples 2x to 2x + 7 of the first row: (0 + (s0 + s1), 0 + (s2 + s3)) and the same for s4 to s7; adding s0 and s1
            // to zero one after the other gives the same sum, of either sign when it is zero
            var samples = unsafe(Vector128.LoadUnsafe(ref firstStart, (nuint)(2 * x)));
            var low = Vector128<double>.Zero + AdvSimd.Arm64.AddPairwise(Vector128.WidenLower(samples), Vector128.WidenUpper(samples));
            samples = unsafe(Vector128.LoadUnsafe(ref firstStart, (nuint)((2 * x) + 4)));
            var high = Vector128<double>.Zero + AdvSimd.Arm64.AddPairwise(Vector128.WidenLower(samples), Vector128.WidenUpper(samples));
            if (twoRows)
            {
                var lower = unsafe(Vector128.LoadUnsafe(ref secondStart, (nuint)(2 * x)));
                var upper = unsafe(Vector128.LoadUnsafe(ref secondStart, (nuint)((2 * x) + 4)));
                var even = AdvSimd.Arm64.UnzipEven(lower, upper);
                var odd = AdvSimd.Arm64.UnzipOdd(lower, upper);
                low += Vector128.WidenLower(even);
                low += Vector128.WidenLower(odd);
                high += Vector128.WidenUpper(even);
                high += Vector128.WidenUpper(odd);
            }

            unsafe { Vector128.Narrow(low * scale, high * scale).StoreUnsafe(ref output, (nuint)x); }
        }

        return count;
    }

    /// <summary>
    /// Subsamples the whole groups of four samples with AVX2: the even and odd samples of a row come from two unpacks, in
    /// the lane order 0, 2, 1, 3, restored by one permutation at the end.
    /// </summary>
    /// <returns>The number of subsampled samples.</returns>
    internal static int DownsampleRowAvx2(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> destination)
    {
        var twoRows = !second.IsEmpty;
        var count = GetDownsampledCount(first, second, destination) & ~3;
        ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
        ref var secondStart = ref unsafe(MemoryMarshal.GetReference(twoRows ? second : first));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var scale = Vector256.Create(twoRows ? 1d / 4 : 1d / 2);
        for (var x = 0; x < count; x += 4)
        {
            // Samples 2x to 2x + 7: (0, 1, 2, 3) and (4, 5, 6, 7) give the pairs of the samples (x, x + 2, x + 1, x + 3)
            var lower = Avx.ConvertToVector256Double(unsafe(Vector128.LoadUnsafe(ref firstStart, (nuint)(2 * x))));
            var upper = Avx.ConvertToVector256Double(unsafe(Vector128.LoadUnsafe(ref firstStart, (nuint)((2 * x) + 4))));
            var sum = Vector256<double>.Zero + Avx.UnpackLow(lower, upper);
            sum += Avx.UnpackHigh(lower, upper);
            if (twoRows)
            {
                lower = Avx.ConvertToVector256Double(unsafe(Vector128.LoadUnsafe(ref secondStart, (nuint)(2 * x))));
                upper = Avx.ConvertToVector256Double(unsafe(Vector128.LoadUnsafe(ref secondStart, (nuint)((2 * x) + 4))));
                sum += Avx.UnpackLow(lower, upper);
                sum += Avx.UnpackHigh(lower, upper);
            }

            unsafe { Avx.ConvertToVector128Single(Avx2.Permute4x64(sum * scale, 0b11_01_10_00)).StoreUnsafe(ref output, (nuint)x); }
        }

        return count;
    }

    /// <summary>
    /// Subsamples the whole groups of eight samples with AVX-512: the even and odd samples of a row come from two unpacks,
    /// in the lane order 0, 4, 1, 5, 2, 6, 3, 7, restored by one permutation at the end.
    /// </summary>
    /// <returns>The number of subsampled samples.</returns>
    internal static int DownsampleRowAvx512(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> destination)
    {
        var twoRows = !second.IsEmpty;
        var count = GetDownsampledCount(first, second, destination) & ~7;
        ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
        ref var secondStart = ref unsafe(MemoryMarshal.GetReference(twoRows ? second : first));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var scale = Vector512.Create(twoRows ? 1d / 4 : 1d / 2);
        var order = Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7);
        for (var x = 0; x < count; x += 8)
        {
            var lower = Avx512F.ConvertToVector512Double(unsafe(Vector256.LoadUnsafe(ref firstStart, (nuint)(2 * x))));
            var upper = Avx512F.ConvertToVector512Double(unsafe(Vector256.LoadUnsafe(ref firstStart, (nuint)((2 * x) + 8))));
            var sum = Vector512<double>.Zero + Avx512F.UnpackLow(lower, upper);
            sum += Avx512F.UnpackHigh(lower, upper);
            if (twoRows)
            {
                lower = Avx512F.ConvertToVector512Double(unsafe(Vector256.LoadUnsafe(ref secondStart, (nuint)(2 * x))));
                upper = Avx512F.ConvertToVector512Double(unsafe(Vector256.LoadUnsafe(ref secondStart, (nuint)((2 * x) + 8))));
                sum += Avx512F.UnpackLow(lower, upper);
                sum += Avx512F.UnpackHigh(lower, upper);
            }

            unsafe { Avx512F.ConvertToVector256Single(Avx512F.PermuteVar8x64(sum * scale, order)).StoreUnsafe(ref output, (nuint)x); }
        }

        return count;
    }

    /// <summary>Gets the number of samples that the rows can give: the destination length, at most half of each row.</summary>
    private static int GetDownsampledCount(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> destination) =>
        Math.Min(destination.Length, Math.Min(first.Length, second.IsEmpty ? first.Length : second.Length) / 2);

    /// <summary>
    /// The encoding state of one JPEG. The frame operation writes SOI and the JFIF APP0
    /// segment, then one metadata segment per <see cref="Encode"/> call (<see cref="JpegMetadataWriter"/>), the tables (DQT,
    /// SOF0, DRI, DHT) and the scan header, then the entropy-coded MCUs in bounded steps: each call loads the source rows of an
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
    /// A baseline JPEG with optimized tables (<see cref="JpegEncoder.OptimizeHuffmanTables"/>) is transformed into the same
    /// coefficients of the whole image after DQT and SOF0; its single scan is then coded twice like a progressive scan: the
    /// symbols of each table are counted one MCU row per call, the tables are built by the procedure of T.81 section K.2
    /// (<see cref="JpegHuffmanCodeBuilder.BuildAnnexK"/>), and the DHT segment, the scan header and the MCUs follow.
    /// </para>
    /// <para>
    /// With a restart interval (<see cref="JpegEncoder.RestartInterval"/>), a DRI segment follows the frame header, and the
    /// MCU that starts an interval (counted from the start of each scan, in raster order) is preceded by the padding of the
    /// previous byte and an RSTn marker; the DC predictions start again, and a progressive scan writes its pending
    /// end-of-band run first. The counting passes reset their predictions at the same MCUs.
    /// </para>
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
        private const byte MarkerSof1 = 0xC1;
        private const byte MarkerSof2 = 0xC2;
        private const byte MarkerSof9 = 0xC9;
        private const byte MarkerSof10 = 0xCA;
        private const byte MarkerDht = 0xC4;
        private const byte MarkerDqt = 0xDB;
        private const byte MarkerSos = 0xDA;
        private const byte MarkerDri = 0xDD;
        private const byte MarkerRst0 = 0xD0;

        // The pad byte (and its stuffed zero) and the RSTn marker that may precede an MCU
        private const int MaxRestartBytes = 4;

        private readonly JpegEncoder _encoder;

        // The sample precision (8 or 12 bits); 12-bit samples have wider coefficients, coded by the "wide" methods
        private readonly int _precision;
        private readonly bool _wide;
        private readonly int _maxBlockBytes;
        private readonly JpegArithmeticScanEncoder? _arithmetic;
        private readonly bool _progressive;
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

        // The distance between two rows of a plane: an odd number of 64-byte cache lines, so that the rows of a block do not
        // fall in the same sets of the data cache (plane widths are often a multiple of 4 KB)
        private readonly int _planeStride;

        // The width of the rows of the (subsampled) chroma planes, and the distance between two of their rows
        private readonly int _chromaWidth;
        private readonly int _chromaStride;
        private readonly ushort[] _luminanceQuantization = new ushort[64];
        private readonly ushort[] _chrominanceQuantization = new ushort[64];
        private readonly double[] _luminanceDivisors = new double[64];
        private readonly double[] _chrominanceDivisors = new double[64];
        private readonly int _restartInterval;

        // With optimized tables, the symbol counts of the luminance DC and AC tables, then of the chrominance ones
        private readonly uint[][] _histograms = [];
        private JpegHuffmanEncoder _luminanceDc;
        private JpegHuffmanEncoder _luminanceAc;
        private JpegHuffmanEncoder? _chrominanceDc;
        private JpegHuffmanEncoder? _chrominanceAc;
        // The number of MCU rows that each worker transforms in a batch
        private const int RowsPerWorker = 4;

        // The worst-case output of the MCUs of a baseline scan coded through one block writer
        private const int BaselineRunBytes = 16 * 1024;

        // The worst-case output of the blocks of a progressive scan coded by one call
        private const int ProgressiveRunBytes = 32 * 1024;

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
            _precision = GetPrecision(_encoder, _sourceFormat, nameof(options));
            _wide = _precision > 8;
            _maxBlockBytes = _wide ? MaxWideBlockBytes : MaxBlockBytes;
            _arithmetic = _encoder.EntropyCoding == JpegEntropyCoding.Arithmetic ? new JpegArithmeticScanEncoder() : null;

            (_grayscale, _h, _v) = GetSampling(_sourceFormat, _encoder.ChromaSubsampling);
            _background = PixelFormats.HasAlpha(_sourceFormat) ? _encoder.BackgroundColor : null;

            // Without a background every pixel is opaque (ValidateFrame): RGBA and BGRA rows are read as they are stored,
            // their alpha ignored, instead of being converted to RGB first. 12-bit samples are reduced from 16-bit rows
            _workFormat = _wide ? (_grayscale ? PixelFormat.Gray16 : PixelFormat.Rgb48)
                : _grayscale ? PixelFormat.Gray8
                : _sourceFormat is PixelFormat.Rgba32 or PixelFormat.Bgra32 && _background is null ? _sourceFormat
                : PixelFormat.Rgb24;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _mcusX = (_width + (8 * _h) - 1) / (8 * _h);
            _mcusY = (_height + (8 * _v) - 1) / (8 * _v);
            _planeWidth = _mcusX * 8 * _h;
            _planeStride = GetPlaneStride(_planeWidth);
            _chromaWidth = _planeWidth / _h;
            _chromaStride = GetPlaneStride(_chromaWidth);
            _blocksPerMcu = _grayscale ? 1 : (_h * _v) + 2;
            _restartInterval = _encoder.RestartInterval;

            // Optimized tables need the symbols of the whole scan before its DHT segment: the coefficients of the whole
            // image are kept, as for the progressive process
            // (always with arithmetic coding, and for 12-bit samples, which the typical tables of Annex K do not cover)
            var progressive = _encoder.Process == JpegEncodingProcess.Progressive;
            _progressive = progressive;
            var optimize = _arithmetic is null && !progressive && (_encoder.OptimizeHuffmanTables || _wide);
            var keepCoefficients = progressive || optimize || _arithmetic is not null;
            if (optimize)
            {
                _histograms = [new uint[256], new uint[256], new uint[256], new uint[256]];
            }

            JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.LuminanceQuantization, _encoder.Quality, _luminanceQuantization);
            JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.ChrominanceQuantization, _encoder.Quality, _chrominanceQuantization);
            JpegForwardDct.CreateDivisors(_luminanceQuantization, _luminanceDivisors);
            JpegForwardDct.CreateDivisors(_chrominanceQuantization, _chrominanceDivisors);
            _luminanceDc = new JpegHuffmanEncoder(JpegEncodingTables.LuminanceDcBits, JpegEncodingTables.LuminanceDcValues);
            _luminanceAc = new JpegHuffmanEncoder(JpegEncodingTables.LuminanceAcBits, JpegEncodingTables.LuminanceAcValues);
            if (!_grayscale)
            {
                _chrominanceDc = new JpegHuffmanEncoder(JpegEncodingTables.ChrominanceDcBits, JpegEncodingTables.ChrominanceDcValues);
                _chrominanceAc = new JpegHuffmanEncoder(JpegEncodingTables.ChrominanceAcBits, JpegEncodingTables.ChrominanceAcValues);
            }

            _metadata = new JpegMetadataWriter(options.Metadata);

            // One MCU row of samples per component, the chroma subsampled row by row from two full-resolution rows per
            // component (one row per row of chroma blocks)
            var planeBytes = (long)_planeStride * 8 * _v * sizeof(float);
            var chromaBytes = (long)_chromaStride * 8 * sizeof(float);
            var chromaRowBytes = _h > 1 ? (long)_planeWidth * 4 * sizeof(float) : 0;
            var rowBytes = (long)_width * PixelFormats.GetBytesPerPixel(_workFormat);
            if (planeBytes > CheckedSizes.MaxBufferLength || chromaRowBytes > CheckedSizes.MaxBufferLength || rowBytes > CheckedSizes.MaxBufferLength)
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
                    _transformWorkers[i] = new TransformWorker(scope, _sourceFormat != _workFormat ? (int)rowBytes : 0, (int)planeBytes, _grayscale ? 0 : (int)chromaBytes, (int)chromaRowBytes);
                }

                if (workerCount > 1 && !keepCoefficients)
                {
                    var batchBytes = (long)_batchRows * _mcusX * _blocksPerMcu * 64 * sizeof(short);
                    if (batchBytes > CheckedSizes.MaxBufferLength)
                        throw CheckedSizes.CreateOverflowException(scope.Limits);

                    _batch = scope.Rent((int)batchBytes, AllocationKind.Temporary, clear: false);
                }

                if (keepCoefficients)
                {
                    // The grid of a scan of one component alone only has the blocks that hold samples of the image
                    _scanEncoder = progressive && _arithmetic is null ? new JpegProgressiveScanEncoder() : null;
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
            OptimizedScan,
            ArithmeticScans,
            Trailer,
        }

        /// <summary>The passes over the blocks of a progressive scan, or of the baseline scan with optimized tables.</summary>
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
                    WriteFrameHeader(output, (_arithmetic is not null, _progressive) switch
                    {
                        (true, true) => MarkerSof10,
                        (true, false) => MarkerSof9,
                        (false, true) => MarkerSof2,
                        _ => _wide ? MarkerSof1 : MarkerSof0,
                    });
                    WriteRestartInterval(output);
                    if (_coefficientPlanes.Length > 0)
                    {
                        // The tables of the scans are computed from the coefficients of the whole image
                        _step = Step.Transform;
                        return false;
                    }

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
                    _step = _arithmetic is not null ? Step.ArithmeticScans : _scanEncoder is not null ? Step.ProgressiveScans : Step.OptimizedScan;
                    return false;

                case Step.ArithmeticScans:
                    if (!EncodeArithmeticScans(output))
                        return false;

                    _step = Step.None;
                    ReleaseBuffers();
                    return true;

                case Step.ProgressiveScans:
                    if (!EncodeProgressiveScans(output))
                        return false;

                    _step = Step.None;
                    ReleaseBuffers();
                    return true;

                case Step.OptimizedScan:
                    if (!EncodeOptimizedScan(output))
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
            payload[0] = (byte)_precision;
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

        /// <summary>Writes the DRI segment of a nonzero restart interval (T.81 B.2.4.4).</summary>
        private void WriteRestartInterval(ImageOutputBuffer output)
        {
            if (_restartInterval == 0)
                return;

            Span<byte> payload = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)_restartInterval);
            JpegMetadataWriter.WriteSegment(output, MarkerDri, payload);
        }

        /// <summary>Gets a value indicating whether a restart interval starts at an MCU of a scan, the MCUs counted from 0 in raster order.</summary>
        private bool IsRestartDue(int mcuIndex) => _restartInterval > 0 && mcuIndex > 0 && mcuIndex % _restartInterval == 0;

        /// <summary>Gets the number of the RSTn marker before an MCU that starts a restart interval: 0 to 7, cyclically.</summary>
        private int GetRestartNumber(int mcuIndex) => ((mcuIndex / _restartInterval) - 1) & 7;

        /// <summary>
        /// Ends the restart interval before an MCU of the baseline scan when one starts there: the last byte is completed
        /// with 1-bits, the RSTn marker follows, and the DC predictions start again from 0 (T.81 F.1.2.3, F.1.1.5.1).
        /// </summary>
        private void WriteRestartIfDue(int mcuIndex, Span<byte> destination, ref int position)
        {
            if (!IsRestartDue(mcuIndex))
                return;

            _bits.Pad(destination, ref position);
            destination[position++] = 0xFF;
            destination[position++] = (byte)(MarkerRst0 + GetRestartNumber(mcuIndex));
            _predictions.AsSpan().Clear();
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
                TransformRows(lease.Storage, _mcuRow, end, (worker, row, mcuX, block) => GetPlaneBlock(row, mcuX, block));

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
                    TransformBlock(single, mcuX, block, GetPlaneBlock(_mcuRow, mcuX, block));
                }
            }
        }

        /// <summary>Gets the coefficients of a block of an MCU of the whole image (progressive frame or optimized tables): the luma blocks in raster order, then Cb and Cr.</summary>
        private Span<short> GetPlaneBlock(int row, int mcuX, int block)
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
            // The AVX-512 and Arm64 transforms read the rows of the plane; for the others the samples are copied first, which
            // measured faster than reading them from the plane rows
            var coefficients = worker.Coefficients.AsSpan();
            var samples = GetBlockSamples(worker, mcuX, block, out var stride);
            var divisors = block < _h * _v ? _luminanceDivisors : _chrominanceDivisors;
            if (_wide)
            {
                // The vector transforms clamp the coefficients to the ranges of 8-bit samples
                JpegForwardDct.TransformAndQuantizeWide(samples, stride, divisors, worker.Scratch, coefficients);
                NarrowCoefficients(coefficients, destination);
                return;
            }

            if (JpegForwardDct.UsesAvx512)
            {
                JpegForwardDct.TransformAndQuantizeAvx512(samples, stride, divisors, destination);
                return;
            }

            if (AdvSimd.Arm64.IsSupported)
            {
                JpegForwardDct.TransformAndQuantizeArm64(samples, stride, divisors, worker.Scratch, destination);
                return;
            }

            if (stride != 8)
            {
                CopyBlock(samples, stride, worker.Block);
                samples = worker.Block;
            }

            JpegForwardDct.TransformAndQuantize(samples, 8, divisors, worker.Scratch, coefficients);
            NarrowCoefficients(coefficients, destination);
        }

        /// <summary>Gets the samples of a block of an MCU of the loaded row of a worker (the luma blocks in raster order, then the Cb and Cr blocks): the rows of its plane.</summary>
        /// <param name="worker">The worker.</param>
        /// <param name="mcuX">The MCU.</param>
        /// <param name="block">The block of the MCU.</param>
        /// <param name="stride">The distance between two rows of the samples.</param>
        private ReadOnlySpan<float> GetBlockSamples(TransformWorker worker, int mcuX, int block, out int stride)
        {
            var lumaBlocks = _h * _v;
            if (block < lumaBlocks)
            {
                stride = _planeStride;
                return worker.PlaneY[((block / _h * 8 * _planeStride) + (mcuX * 8 * _h) + (block % _h * 8))..];
            }

            stride = _chromaStride;
            return (block == lumaBlocks ? worker.PlaneCb : worker.PlaneCr)[(mcuX * 8)..];
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
            var blocksPerMcu = interleaved ? (_h * _v) + 2 : 1;
            var position = 0;
            switch (_scanPass)
            {
                case ScanPass.Count:
                    for (var x = 0; x < columns;)
                    {
                        var index = (_mcuRow * columns) + x;
                        if (IsRestartDue(index))
                        {
                            scanEncoder.Restart(GetRestartNumber(index), default, ref position);
                        }

                        var count = GetProgressiveRunLength(scan, index, columns - x, blocksPerMcu);
                        CodeProgressiveMcus(scan, _mcuRow, x, count, default, ref position);
                        x += count;
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

            var coded = 0;
            while (_mcuRow < rows)
            {
                while (_mcuX < columns)
                {
                    // Bounded output per call: stop once the buffer should be flushed (at least one MCU per call)
                    if (coded > 0 && output.ShouldFlush)
                        return false;

                    // A restart first writes the pending run of blocks, which is bounded like the one a block can write
                    var index = (_mcuRow * columns) + _mcuX;
                    var restart = IsRestartDue(index);
                    var count = GetProgressiveRunLength(scan, index, columns - _mcuX, blocksPerMcu);
                    var span = output.GetSpan(JpegProgressiveScanEncoder.GetMaxBytes(scan, count * blocksPerMcu) + (restart ? JpegProgressiveScanEncoder.MaxBlockBytes : 0));
                    position = 0;
                    if (restart)
                    {
                        scanEncoder.Restart(GetRestartNumber(index), span, ref position);
                    }

                    CodeProgressiveMcus(scan, _mcuRow, _mcuX, count, span, ref position);
                    output.Advance(position);
                    _mcuX += count;
                    coded += count;
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

        /// <summary>
        /// Gets the number of MCUs of a progressive scan coded by one call from the MCU <paramref name="index"/>: at most
        /// <paramref name="remaining"/> (the rest of the line), the MCUs whose worst-case output fits in
        /// <see cref="ProgressiveRunBytes"/>, and the MCUs before the next restart marker.
        /// </summary>
        private int GetProgressiveRunLength(JpegProgressiveScan scan, int index, int remaining, int blocksPerMcu)
        {
            var count = Math.Min(remaining, Math.Max(1, ProgressiveRunBytes / (blocksPerMcu * JpegProgressiveScanEncoder.GetMaxBlockBytes(scan))));
            if (_restartInterval > 0)
            {
                count = Math.Min(count, _restartInterval - (index % _restartInterval));
            }

            return count;
        }

        /// <summary>Codes consecutive MCUs of a line of a progressive scan: the blocks of every component for an interleaved scan, else one block each (T.81 A.2).</summary>
        private void CodeProgressiveMcus(JpegProgressiveScan scan, int row, int column, int count, Span<byte> destination, ref int position)
        {
            var scanEncoder = _scanEncoder!;
            if (scan.ComponentCount == 1)
            {
                scanEncoder.CodeBlocks(scan.FirstComponent, _coefficientPlanes[scan.FirstComponent]!.GetBlocks(row, column, count), destination, ref position);
                return;
            }

            var luminance = _coefficientPlanes[0]!;
            scanEncoder.CodeInterleavedDcBlocks(
                luminance.GetBlocks(row * _v, column * _h, count * _h),
                _v == 2 ? luminance.GetBlocks((row * _v) + 1, column * _h, count * _h) : default,
                _coefficientPlanes[1]!.GetBlocks(row, column, count),
                _coefficientPlanes[2]!.GetBlocks(row, column, count),
                _h,
                _v,
                count,
                destination,
                ref position);
        }

        /// <summary>
        /// Writes the scans of an arithmetic-coded frame (SOF9: one interleaved scan of every coefficient; SOF10: the scans
        /// of the progressive script) from the coefficients of the whole image, one MCU row per call: the scan header, then
        /// the entropy-coded data of each row with its restart markers.
        /// </summary>
        /// <returns><see langword="true"/> when the last scan is written.</returns>
        private bool EncodeArithmeticScans(ImageOutputBuffer output)
        {
            var arithmetic = _arithmetic!;
            var scanCount = _progressive ? JpegProgressiveScanEncoder.GetScript(_grayscale).Length : 1;
            var scan = _progressive
                ? JpegProgressiveScanEncoder.GetScript(_grayscale)[_scanIndex]
                : new JpegProgressiveScan(FirstComponent: 0, ComponentCount: (byte)(_grayscale ? 1 : 3), Ss: 0, Se: 63, Ah: 0, Al: 0);
            if (_scanPass == ScanPass.None)
            {
                WriteScanHeader(output, scan.FirstComponent, scan.ComponentCount, dcTables: scan.Ss == 0 && scan.Ah == 0, acTables: scan.Se > 0, scan.Ss, scan.Se, (scan.Ah << 4) | scan.Al);
                arithmetic.BeginScan();
                _scanPass = ScanPass.Write;
                _mcuRow = 0;
                return false;
            }

            var interleaved = scan.ComponentCount > 1;
            var rows = interleaved ? _mcusY : _coefficientPlanes[scan.FirstComponent]!.ScanBlockRows;
            var columns = interleaved ? _mcusX : _coefficientPlanes[scan.FirstComponent]!.ScanBlocksPerLine;
            for (var x = 0; x < columns; x++)
            {
                var index = (_mcuRow * columns) + x;
                if (IsRestartDue(index))
                {
                    arithmetic.Restart(GetRestartNumber(index));
                }

                if (interleaved)
                {
                    for (var block = 0; block < _blocksPerMcu; block++)
                    {
                        var component = block < _h * _v ? 0 : block - (_h * _v) + 1;
                        EncodeArithmeticBlock(arithmetic, scan, component, GetPlaneBlock(_mcuRow, x, block));
                    }
                }
                else
                {
                    EncodeArithmeticBlock(arithmetic, scan, scan.FirstComponent, _coefficientPlanes[scan.FirstComponent]!.GetBlock(_mcuRow, x));
                }
            }

            _mcuRow++;
            var last = _mcuRow == rows;
            if (last)
            {
                arithmetic.EndScan();
            }

            output.Write(arithmetic.Encoder.Output);
            arithmetic.Encoder.ClearOutput();
            CancellationToken.ThrowIfCancellationRequested();
            if (!last)
                return false;

            _mcuRow = 0;
            _scanPass = ScanPass.None;
            _scanIndex++;
            return _scanIndex == scanCount;
        }

        /// <summary>Codes the part of a block that belongs to an arithmetic-coded scan.</summary>
        private static void EncodeArithmeticBlock(JpegArithmeticScanEncoder arithmetic, JpegProgressiveScan scan, int component, ReadOnlySpan<short> block)
        {
            if (scan.Ss == 0 && scan.Se == 63)
            {
                arithmetic.EncodeSequentialBlock(component, block);
            }
            else if (scan.Ss == 0)
            {
                if (scan.Ah == 0)
                {
                    arithmetic.EncodeDcFirst(component, block, scan.Al);
                }
                else
                {
                    arithmetic.EncodeDcRefinement(block, scan.Al);
                }
            }
            else if (scan.Ah == 0)
            {
                arithmetic.EncodeAcFirst(JpegArithmeticScanEncoder.GetTableIndex(component), block, scan.Ss, scan.Se, scan.Al);
            }
            else
            {
                arithmetic.EncodeAcRefinement(JpegArithmeticScanEncoder.GetTableIndex(component), block, scan.Ss, scan.Se, scan.Al);
            }
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

                        coded += EncodeMcuRun(output, stored: batched);
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
        /// level-shifted Y, Cb, Cr samples, the last column replicated to the padded width. With subsampling, the Cb and Cr
        /// samples of the rows of a row of chroma blocks are box-filtered (<see cref="DownsampleRow"/>) once they are all converted.
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

                var offset = r * _planeStride;
                if (_grayscale)
                {
                    if (_wide)
                    {
                        ConvertWideGrayRow(source, planeY.Slice(offset, _planeWidth));
                    }
                    else
                    {
                        ConvertGrayRow(source, planeY.Slice(offset, _planeWidth));
                    }
                }
                else if (_h == 1)
                {
                    ConvertColorRow(source, planeY.Slice(offset, _planeWidth), planeCb.Slice(offset, _planeWidth), planeCr.Slice(offset, _planeWidth));
                }
                else
                {
                    var pair = r % _v;
                    ConvertColorRow(source, planeY.Slice(offset, _planeWidth), worker.GetChromaRow(0, pair, _planeWidth), worker.GetChromaRow(1, pair, _planeWidth));
                    if (pair == _v - 1)
                    {
                        var chromaOffset = r / _v * _chromaStride;
                        DownsampleRow(worker.GetChromaRow(0, 0, _planeWidth), _v == 2 ? worker.GetChromaRow(0, 1, _planeWidth) : default, planeCb.Slice(chromaOffset, _chromaWidth));
                        DownsampleRow(worker.GetChromaRow(1, 0, _planeWidth), _v == 2 ? worker.GetChromaRow(1, 1, _planeWidth) : default, planeCr.Slice(chromaOffset, _chromaWidth));
                    }
                }
            }
        }

        /// <summary>Gets the distance between two rows of a plane: an odd number of 16 samples (64-byte cache lines).</summary>
        private static int GetPlaneStride(int width) => (((width + 15) / 16) | 1) * 16;

        /// <summary>Converts a row of 16-bit gray samples to level-shifted 12-bit samples (<see cref="ReduceToTwelveBits"/>, minus 2048).</summary>
        private void ConvertWideGrayRow(ReadOnlySpan<byte> source, Span<float> luma)
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(source));
            for (var x = 0; x < _width; x++)
            {
                luma[x] = ReduceToTwelveBits(samples[x]) - 2048f;
            }

            luma[_width..].Fill(luma[_width - 1]);
        }

        /// <summary>
        /// Converts a row of 16-bit RGB samples, reduced to 12 bits (<see cref="ReduceToTwelveBits"/>), to level-shifted
        /// JFIF 1.02 full-range YCbCr samples with the formula of the 8-bit samples (<see cref="ConvertColorRow"/>) and a
        /// level shift of 2048, computed in double precision and stored unrounded (single precision).
        /// </summary>
        private void ConvertWideColorRow(ReadOnlySpan<ushort> source, Span<float> luma, Span<float> blue, Span<float> red)
        {
            for (var x = 0; x < _width; x++)
            {
                double r = ReduceToTwelveBits(source[3 * x]);
                double g = ReduceToTwelveBits(source[(3 * x) + 1]);
                double b = ReduceToTwelveBits(source[(3 * x) + 2]);
                var y = (0.299 * r) + (0.587 * g) + (0.114 * b);
                luma[x] = (float)(y - 2048);
                blue[x] = (float)((b - y) * BlueScale);
                red[x] = (float)((r - y) * RedScale);
            }
        }

        /// <summary>Reduces a 16-bit sample to 12 bits, with nearest rounding: 8-bit samples scaled to 16 bits (times 257) give their exact 12-bit scaling.</summary>
        private static int ReduceToTwelveBits(ushort value) => ((value * 4095) + 32767) / 65535;

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
            switch (_workFormat)
            {
                case PixelFormat.Rgb48:
                    ConvertWideColorRow(unsafe(MemoryMarshal.Cast<byte, ushort>(source)), luma, blue, red);
                    break;
                case PixelFormat.Rgba32:
                    ConvertRgbToYCbCr<JpegRgbaLayout>(source, luma[.._width], blue[.._width], red[.._width]);
                    break;
                case PixelFormat.Bgra32:
                    ConvertRgbToYCbCr<JpegBgraLayout>(source, luma[.._width], blue[.._width], red[.._width]);
                    break;
                default:
                    ConvertRgbToYCbCr<JpegRgbLayout>(source, luma[.._width], blue[.._width], red[.._width]);
                    break;
            }

            luma[_width..].Fill(luma[_width - 1]);
            blue[_width..].Fill(blue[_width - 1]);
            red[_width..].Fill(red[_width - 1]);
        }

        /// <summary>
        /// Codes consecutive MCUs of the current row from <see cref="_mcuX"/>, through one <see cref="JpegBlockBitWriter"/>
        /// (one byte stuffing pass): at most the MCUs whose worst-case output fits in <see cref="BaselineRunBytes"/>, and the
        /// MCUs before the next restart marker, which is written first when it is due.
        /// </summary>
        /// <param name="output">The output.</param>
        /// <param name="stored"><see langword="true"/> to code stored coefficients; <see langword="false"/> to transform the loaded row of the only worker.</param>
        /// <returns>The number of MCUs coded.</returns>
        private int EncodeMcuRun(ImageOutputBuffer output, bool stored)
        {
            var index = (_mcuRow * _mcusX) + _mcuX;
            var count = Math.Min(_mcusX - _mcuX, Math.Max(1, BaselineRunBytes / (_blocksPerMcu * _maxBlockBytes)));
            if (_restartInterval > 0)
            {
                count = Math.Min(count, _restartInterval - (index % _restartInterval));
            }

            var span = output.GetSpan((count * _blocksPerMcu * _maxBlockBytes) + MaxRestartBytes + JpegBlockBitWriter.SpeculativeBytes);
            var position = 0;
            WriteRestartIfDue(index, span, ref position);
            var writer = new JpegBlockBitWriter(span, position, _bits);
            if (!stored && (JpegForwardDct.UsesAvx512 || AdvSimd.Arm64.IsSupported))
            {
                TransformAndEncodeMcus(count, ref writer);
            }
            else
            {
                for (var mcuX = _mcuX; mcuX < _mcuX + count; mcuX++)
                {
                    if (stored)
                    {
                        EncodeStoredMcu(_mcuRow, mcuX, ref writer);
                    }
                    else
                    {
                        EncodeMcu(mcuX, ref writer);
                    }
                }
            }

            output.Advance(writer.Complete(ref _bits));
            _mcuX += count;
            return count;
        }

        /// <summary>
        /// Transforms the blocks of <paramref name="count"/> MCUs of the loaded row of the only worker from <see cref="_mcuX"/>
        /// into 16-bit coefficients (<see cref="TransformBlock"/>), then codes them: the transforms of consecutive blocks
        /// overlap, which they do not when each block is coded right after its transform (measured with AVX-512 and Arm64).
        /// </summary>
        private void TransformAndEncodeMcus(int count, ref JpegBlockBitWriter writer)
        {
            var worker = _transformWorkers[0];
            var run = worker.GetRun(count * _blocksPerMcu);
            for (var mcu = 0; mcu < count; mcu++)
            {
                for (var block = 0; block < _blocksPerMcu; block++)
                {
                    TransformBlock(worker, _mcuX + mcu, block, run.Slice(((mcu * _blocksPerMcu) + block) * 64, 64));
                }
            }

            var lumaBlocks = _h * _v;
            for (var mcu = 0; mcu < count; mcu++)
            {
                for (var block = 0; block < _blocksPerMcu; block++)
                {
                    var coefficients = run.Slice(((mcu * _blocksPerMcu) + block) * 64, 64);
                    if (block < lumaBlocks)
                    {
                        EncodeBlock<short>(ref writer, coefficients, component: 0, _luminanceDc, _luminanceAc);
                    }
                    else
                    {
                        EncodeBlock<short>(ref writer, coefficients, component: block - lumaBlocks + 1, _chrominanceDc!, _chrominanceAc!);
                    }
                }
            }
        }

        /// <summary>Transforms and codes an MCU of the loaded row of the only worker.</summary>
        private void EncodeMcu(int mcuX, ref JpegBlockBitWriter writer)
        {
            var worker = _transformWorkers[0];
            var lumaBlocks = _h * _v;
            for (var block = 0; block < _blocksPerMcu; block++)
            {
                var samples = GetBlockSamples(worker, mcuX, block, out var stride);
                if (block < lumaBlocks)
                {
                    TransformAndEncodeBlock(worker, samples, stride, ref writer, component: 0, _luminanceDivisors, _luminanceDc, _luminanceAc);
                }
                else
                {
                    TransformAndEncodeBlock(worker, samples, stride, ref writer, component: block - lumaBlocks + 1, _chrominanceDivisors, _chrominanceDc!, _chrominanceAc!);
                }
            }
        }

        private void TransformAndEncodeBlock(TransformWorker worker, ReadOnlySpan<float> samples, int stride, ref JpegBlockBitWriter writer, int component, ReadOnlySpan<double> divisors, JpegHuffmanEncoder dc, JpegHuffmanEncoder ac)
        {
            var coefficients = worker.Coefficients.AsSpan();
            JpegForwardDct.TransformAndQuantize(samples, stride, divisors, worker.Scratch, coefficients);
            EncodeBlock<int>(ref writer, coefficients, component, dc, ac);
        }

        /// <summary>Codes an MCU from its stored coefficients: those of the baseline batch, or of the whole image with optimized tables.</summary>
        private void EncodeStoredMcu(int row, int mcuX, ref JpegBlockBitWriter writer)
        {
            for (var block = 0; block < _blocksPerMcu; block++)
            {
                var component = block < _h * _v ? 0 : block - (_h * _v) + 1;
                var dc = component == 0 ? _luminanceDc : _chrominanceDc!;
                var ac = component == 0 ? _luminanceAc : _chrominanceAc!;
                if (_wide)
                {
                    EncodeWideBlock(ref writer, GetStoredBlock(row, mcuX, block), component, dc, ac);
                }
                else
                {
                    EncodeBlock<short>(ref writer, GetStoredBlock(row, mcuX, block), component, dc, ac);
                }
            }
        }

        /// <summary>Gets the stored coefficients of a block of an MCU: those of the baseline batch, or of the whole image with optimized tables.</summary>
        private Span<short> GetStoredBlock(int row, int mcuX, int block) => _batch is not null ? GetBatchBlock(row, mcuX, block) : GetPlaneBlock(row, mcuX, block);

        /// <summary>
        /// Codes the baseline scan with tables computed from the image (<see cref="JpegEncoder.OptimizeHuffmanTables"/>):
        /// the symbols of the stored coefficients are counted one MCU row per call, then the DHT segment of the tables
        /// built from the counts and the scan header are written, then the MCUs in bounded steps, with the same code path as
        /// the batches of several workers.
        /// </summary>
        /// <returns><see langword="true"/> when the last MCU was coded and the entropy-coded segment padded.</returns>
        private bool EncodeOptimizedScan(ImageOutputBuffer output)
        {
            switch (_scanPass)
            {
                case ScanPass.None:
                    _predictions.AsSpan().Clear();
                    _scanPass = ScanPass.Count;
                    goto case ScanPass.Count;

                case ScanPass.Count:
                    for (var mcuX = 0; mcuX < _mcusX; mcuX++)
                    {
                        if (IsRestartDue((_mcuRow * _mcusX) + mcuX))
                        {
                            _predictions.AsSpan().Clear();
                        }

                        for (var block = 0; block < _blocksPerMcu; block++)
                        {
                            var component = block < _h * _v ? 0 : block - (_h * _v) + 1;
                            if (_wide)
                            {
                                CountWideBlock(GetStoredBlock(_mcuRow, mcuX, block), component);
                            }
                            else
                            {
                                CountBlock(GetStoredBlock(_mcuRow, mcuX, block), component);
                            }
                        }
                    }

                    CancellationToken.ThrowIfCancellationRequested();
                    if (++_mcuRow < _mcusY)
                        return false;

                    // Every table of the scan codes at least one symbol per block: a DC category and an EOB or a last coefficient
                    _luminanceDc = JpegHuffmanCodeBuilder.BuildAnnexK(_histograms[0]);
                    _luminanceAc = JpegHuffmanCodeBuilder.BuildAnnexK(_histograms[1]);
                    if (!_grayscale)
                    {
                        _chrominanceDc = JpegHuffmanCodeBuilder.BuildAnnexK(_histograms[2]);
                        _chrominanceAc = JpegHuffmanCodeBuilder.BuildAnnexK(_histograms[3]);
                    }

                    _mcuRow = 0;
                    _scanPass = ScanPass.Header;
                    return false;

                case ScanPass.Header:
                    WriteHuffmanTables(output);
                    WriteScanHeader(output, firstComponent: 0, _grayscale ? 1 : 3, dcTables: true, acTables: true, spectralStart: 0, spectralEnd: 63, approximation: 0);
                    _predictions.AsSpan().Clear();
                    _scanPass = ScanPass.Write;
                    return false;
            }

            var coded = 0;
            while (_mcuRow < _mcusY)
            {
                while (_mcuX < _mcusX)
                {
                    // Bounded output per call: stop once the buffer should be flushed (at least one MCU per call)
                    if (coded > 0 && output.ShouldFlush)
                        return false;

                    coded += EncodeMcuRun(output, stored: true);
                }

                _mcuX = 0;
                _mcuRow++;
                CancellationToken.ThrowIfCancellationRequested();
            }

            var end = output.GetSpan(2);
            var endPosition = 0;
            _bits.Pad(end, ref endPosition);
            output.Advance(endPosition);
            _scanPass = ScanPass.None;
            return true;
        }

        /// <summary>Counts the symbols that <see cref="EncodeBlock"/> codes for a block of a component, in the tables of the component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CountBlock(ReadOnlySpan<short> coefficients, int component)
        {
            var dc = _histograms[component == 0 ? 0 : 2];
            var ac = _histograms[component == 0 ? 1 : 3];
            var difference = coefficients[0] - _predictions[component];
            _predictions[component] = coefficients[0];
            dc[GetSizeCategory(difference)]++;
            var mask = GetNonZeroMask(coefficients) & ~1UL;
            var previous = 0;
            while (mask != 0)
            {
                var k = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                var run = k - previous - 1;
                while (run > 15)
                {
                    ac[0xF0]++;
                    run -= 16;
                }

                ac[(run << 4) | GetSizeCategory(coefficients[k])]++;
                previous = k;
            }

            if (previous < 63)
            {
                ac[0x00]++;
            }
        }

        /// <summary>Counts the symbols that <see cref="EncodeWideBlock"/> codes for a block of 12-bit samples.</summary>
        private void CountWideBlock(ReadOnlySpan<short> coefficients, int component)
        {
            var dc = _histograms[component == 0 ? 0 : 2];
            var ac = _histograms[component == 0 ? 1 : 3];
            var difference = coefficients[0] - _predictions[component];
            _predictions[component] = coefficients[0];
            dc[GetWideSizeCategory(difference)]++;
            var mask = GetNonZeroMask(coefficients) & ~1UL;
            var previous = 0;
            while (mask != 0)
            {
                var k = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                var run = k - previous - 1;
                while (run > 15)
                {
                    ac[0xF0]++;
                    run -= 16;
                }

                ac[(run << 4) | GetWideSizeCategory(coefficients[k])]++;
                previous = k;
            }

            if (previous < 63)
            {
                ac[0x00]++;
            }
        }

        /// <summary>
        /// Codes the quantized coefficients of a block of 12-bit samples (zig-zag order), as <see cref="EncodeBlock"/> does:
        /// DC differences have up to 16 bits and AC coefficients up to 15, beyond the <see cref="ValueBits"/> table.
        /// </summary>
        private void EncodeWideBlock(ref JpegBlockBitWriter writer, ReadOnlySpan<short> coefficients, int component, JpegHuffmanEncoder dc, JpegHuffmanEncoder ac)
        {
            var difference = coefficients[0] - _predictions[component];
            _predictions[component] = coefficients[0];
            WriteWideValue(ref writer, dc, symbolHigh: 0, difference);
            var mask = GetNonZeroMask(coefficients) & ~1UL;
            var previous = 0;
            while (mask != 0)
            {
                var k = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                var run = k - previous - 1;
                while (run > 15)
                {
                    WriteSymbol(ref writer, ac, 0xF0);
                    run -= 16;
                }

                WriteWideValue(ref writer, ac, run, coefficients[k]);
                previous = k;
            }

            if (previous < 63)
            {
                WriteSymbol(ref writer, ac, 0x00);
            }
        }

        /// <summary>Writes the symbol <c>(symbolHigh &lt;&lt; 4) | SSSS</c> for a value of any size up to 15 bits, then its SSSS additional bits (negative values as <c>value - 1</c>).</summary>
        private static void WriteWideValue(ref JpegBlockBitWriter writer, JpegHuffmanEncoder table, int symbolHigh, int value)
        {
            var size = GetWideSizeCategory(value);
            var symbol = (symbolHigh << 4) | size;
            var code = table.GetPackedCode(symbol);
            if (code == 0)
                ThrowMissingCode(symbol);

            var bits = (uint)(value < 0 ? value - 1 : value) & ((1u << size) - 1);

            // At most 16 + 15 bits
            writer.Write(((code >> 5) << size) | bits, (int)(code & 31) + size);
        }

        /// <summary>Gets the size category SSSS of any value: the number of bits of its magnitude (T.81 F.1.2.1.1).</summary>
        private static int GetWideSizeCategory(int value) => 32 - BitOperations.LeadingZeroCount((uint)Math.Abs(value));

        /// <summary>Gets the size category SSSS of a value, -2047 to 2047: the number of bits of its magnitude (T.81 F.1.2.1.1).</summary>
        private static int GetSizeCategory(int value) => (int)(ValueBits[value + 2048] >> 16);

        /// <summary>Codes the quantized coefficients (zig-zag order) of a block of a component.</summary>
        /// <remarks>
        /// The bits go through a <see cref="JpegBlockBitWriter"/>, which stuffs the run of MCUs once it is coded: the
        /// destination has <see cref="MaxBlockBytes"/> per block and <see cref="JpegBlockBitWriter.SpeculativeBytes"/> bytes.
        /// </remarks>
        /// <typeparam name="T">The type of the coefficients: <see cref="int"/> when they come from the forward DCT, <see cref="short"/> when they are stored.</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EncodeBlock<T>(ref JpegBlockBitWriter bits, ReadOnlySpan<T> coefficients, int component, JpegHuffmanEncoder dc, JpegHuffmanEncoder ac)
            where T : unmanaged, IBinaryInteger<T>
        {
            // DC: difference with the previous block of the component, size category then the additional bits (T.81 F.1.2.1)
            var first = int.CreateTruncating(coefficients[0]);
            var difference = first - _predictions[component];
            _predictions[component] = first;
            WriteValue(ref bits, dc, symbolHigh: 0, difference);

            // AC: run lengths of zeros (ZRL for 16), EOB after the last nonzero coefficient (T.81 F.1.2.2); the nonzero
            // coefficients are visited through a bit mask
            var nonZero = typeof(T) == typeof(short) ? GetNonZeroMask(unsafe(MemoryMarshal.Cast<T, short>(coefficients))) : GetNonZeroMask(unsafe(MemoryMarshal.Cast<T, int>(coefficients)));
            var mask = nonZero & ~1UL;
            var previous = 0;
            while (mask != 0)
            {
                var k = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                var run = k - previous - 1;
                while (run > 15)
                {
                    WriteSymbol(ref bits, ac, 0xF0);
                    run -= 16;
                }

                WriteValue(ref bits, ac, run, int.CreateTruncating(coefficients[k]));
                previous = k;
            }

            if (previous < 63)
            {
                WriteSymbol(ref bits, ac, 0x00);
            }
        }

        /// <summary>
        /// Writes the symbol <c>(symbolHigh &lt;&lt; 4) | SSSS</c> for a value, then its SSSS additional bits (negative values as
        /// <c>value - 1</c>), as one write: the size category and the additional bits come from <see cref="ValueBits"/>.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="table">The table.</param>
        /// <param name="symbolHigh">The high 4 bits of the symbol.</param>
        /// <param name="value">The value, -2047 to 2047.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteValue(ref JpegBlockBitWriter writer, JpegHuffmanEncoder table, int symbolHigh, int value)
        {
            var valueBits = ValueBits[value + 2048];
            var size = (int)(valueBits >> 16);
            var symbol = (symbolHigh << 4) | size;
            var code = table.GetPackedCode(symbol);
            if (code == 0)
                ThrowMissingCode(symbol);

            // At most 16 + 16 bits
            writer.Write(((code >> 5) << size) | (valueBits & 0xFFFF), (int)(code & 31) + size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteSymbol(ref JpegBlockBitWriter writer, JpegHuffmanEncoder table, int symbol)
        {
            var code = table.GetPackedCode(symbol);
            if (code == 0)
                ThrowMissingCode(symbol);

            writer.Write(code >> 5, (int)(code & 31));
        }

        private static void ThrowMissingCode(int symbol) => throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The Huffman table has no code for the symbol 0x{symbol:X2}."));

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
            private PooledBuffer? _chromaRows;
            private short[] _run = [];

            /// <param name="scope">The scope the buffers are rented from.</param>
            /// <param name="rowBytes">The size of the converted source row, or 0 when the source rows are used as they are.</param>
            /// <param name="planeBytes">The size of an MCU row of luma samples.</param>
            /// <param name="chromaBytes">The size of an MCU row of samples of a chroma component, or 0 for grayscale output.</param>
            /// <param name="chromaRowBytes">The size of the full-resolution chroma rows to subsample, or 0 without subsampling.</param>
            public TransformWorker(AllocationScope scope, int rowBytes, int planeBytes, int chromaBytes, int chromaRowBytes)
            {
                try
                {
                    if (rowBytes > 0)
                    {
                        _row = scope.Rent(rowBytes, AllocationKind.Temporary, clear: false);
                    }

                    _planeY = scope.Rent(planeBytes, AllocationKind.Temporary, clear: false);
                    if (chromaBytes > 0)
                    {
                        _planeCb = scope.Rent(chromaBytes, AllocationKind.Temporary, clear: false);
                        _planeCr = scope.Rent(chromaBytes, AllocationKind.Temporary, clear: false);
                    }

                    if (chromaRowBytes > 0)
                    {
                        _chromaRows = scope.Rent(chromaRowBytes, AllocationKind.Temporary, clear: false);
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

            /// <summary>Gets a full-resolution row of chroma samples to subsample.</summary>
            /// <param name="component">0 for Cb, 1 for Cr.</param>
            /// <param name="index">The row of the pair, 0 or 1.</param>
            /// <param name="width">The width of the rows.</param>
            public Span<float> GetChromaRow(int component, int index, int width) => unsafe(MemoryMarshal.Cast<byte, float>(_chromaRows!.Span)).Slice(((2 * component) + index) * width, width);

            public float[] Block { get; } = new float[64];

            public double[] Scratch { get; } = new double[64];

            public int[] Coefficients { get; } = new int[64];

            /// <summary>Gets the coefficients of a run of blocks, transformed before they are coded (see <see cref="TransformAndEncodeMcus"/>).</summary>
            /// <param name="blocks">The number of blocks.</param>
            public Span<short> GetRun(int blocks)
            {
                if (_run.Length < blocks * 64)
                {
                    _run = new short[blocks * 64];
                }

                return _run.AsSpan(0, blocks * 64);
            }

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
                _chromaRows?.Dispose();
                _chromaRows = null;
            }
        }

        /// <summary>Gets where the coefficients of a block of an MCU go.</summary>
        private delegate Span<short> BlockDestination(TransformWorker worker, int row, int mcuX, int block);
    }
}
