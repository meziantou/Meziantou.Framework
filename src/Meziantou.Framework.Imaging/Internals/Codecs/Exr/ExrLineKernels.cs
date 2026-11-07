using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The line writers of the most common OpenEXR layouts (<see cref="ExrDecoder"/>): red, green and blue HALF channels,
/// with or without a HALF alpha channel, written to the pixels of the source layout.
/// </summary>
/// <remarks>
/// The <c>...Scalar</c> versions are the reference, the operations of the general line writer of the decoder. The vector
/// paths give the same bits: the halves are converted exactly (normal and subnormal numbers; a vector with an infinity or
/// a value that is not a number takes the scalar path), and a color divided by its alpha in single precision is the
/// rounded quotient in double precision rounded to single precision (a double rounding that is innocuous for a quotient:
/// 53 bits are more than twice 24 bits plus 2).
/// </remarks>
internal static class ExrLineKernels
{
    private const ushort HalfOne = 0x3C00;
    private const ushort HalfMagnitude = 0x7FFF;
    private const ushort HalfInfinity = 0x7C00;

    private static bool UseVectors => BitConverter.IsLittleEndian && (AdvSimd.Arm64.IsSupported || Sse2.IsSupported);

    /// <summary>Writes RGB HALF samples to <see cref="RgbaHalf"/> pixels of alpha 1.</summary>
    /// <param name="red">The red samples.</param>
    /// <param name="green">The green samples (as many as <paramref name="red"/>).</param>
    /// <param name="blue">The blue samples (as many as <paramref name="red"/>).</param>
    /// <param name="pixels">Receives the pixels, four halves each.</param>
    public static void WriteHalfPixels(ReadOnlySpan<ushort> red, ReadOnlySpan<ushort> green, ReadOnlySpan<ushort> blue, Span<ushort> pixels)
    {
        var count = red.Length;
        _ = green[..count];
        _ = blue[..count];
        _ = pixels[..(count * 4)];
        var x = 0;
        if (UseVectors)
        {
            ref var r = ref unsafe(MemoryMarshal.GetReference(red));
            ref var g = ref unsafe(MemoryMarshal.GetReference(green));
            ref var b = ref unsafe(MemoryMarshal.GetReference(blue));
            ref var p = ref unsafe(MemoryMarshal.GetReference(pixels));
            var one = Vector128.Create(HalfOne);
            for (; x + Vector128<ushort>.Count <= count; x += Vector128<ushort>.Count)
            {
                var rv = unsafe(Vector128.LoadUnsafe(ref r, (nuint)x));
                var gv = unsafe(Vector128.LoadUnsafe(ref g, (nuint)x));
                var bv = unsafe(Vector128.LoadUnsafe(ref b, (nuint)x));
                var rgLow = ZipLow(rv, gv).AsUInt32();
                var rgHigh = ZipHigh(rv, gv).AsUInt32();
                var baLow = ZipLow(bv, one).AsUInt32();
                var baHigh = ZipHigh(bv, one).AsUInt32();
                var offset = (nuint)x * 4;
                unsafe { ZipLow(rgLow, baLow).AsUInt16().StoreUnsafe(ref p, offset); }
                unsafe { ZipHigh(rgLow, baLow).AsUInt16().StoreUnsafe(ref p, offset + 8); }
                unsafe { ZipLow(rgHigh, baHigh).AsUInt16().StoreUnsafe(ref p, offset + 16); }
                unsafe { ZipHigh(rgHigh, baHigh).AsUInt16().StoreUnsafe(ref p, offset + 24); }
            }
        }

        WriteHalfPixelsScalar(red[x..], green[x..], blue[x..], pixels[(x * 4)..]);
    }

    /// <summary>The scalar reference of <see cref="WriteHalfPixels"/>.</summary>
    /// <param name="red">The red samples.</param>
    /// <param name="green">The green samples (as many as <paramref name="red"/>).</param>
    /// <param name="blue">The blue samples (as many as <paramref name="red"/>).</param>
    /// <param name="pixels">Receives the pixels, four halves each.</param>
    internal static void WriteHalfPixelsScalar(ReadOnlySpan<ushort> red, ReadOnlySpan<ushort> green, ReadOnlySpan<ushort> blue, Span<ushort> pixels)
    {
        for (var x = 0; x < red.Length; x++)
        {
            pixels[(x * 4) + 0] = red[x];
            pixels[(x * 4) + 1] = green[x];
            pixels[(x * 4) + 2] = blue[x];
            pixels[(x * 4) + 3] = HalfOne;
        }
    }

    /// <summary>Writes RGBA HALF samples to <see cref="RgbaFloat"/> pixels, the colors divided by alpha (see <see cref="ExrDecoder.Unpremultiply"/>).</summary>
    /// <param name="red">The red samples.</param>
    /// <param name="green">The green samples (as many as <paramref name="red"/>).</param>
    /// <param name="blue">The blue samples (as many as <paramref name="red"/>).</param>
    /// <param name="alpha">The alpha samples (as many as <paramref name="red"/>).</param>
    /// <param name="pixels">Receives the pixels, four floats each.</param>
    public static void WriteFloatPixels(ReadOnlySpan<ushort> red, ReadOnlySpan<ushort> green, ReadOnlySpan<ushort> blue, ReadOnlySpan<ushort> alpha, Span<float> pixels)
    {
        var count = red.Length;
        _ = green[..count];
        _ = blue[..count];
        _ = alpha[..count];
        _ = pixels[..(count * 4)];
        var x = 0;
        if (UseVectors)
        {
            ref var r = ref unsafe(MemoryMarshal.GetReference(red));
            ref var g = ref unsafe(MemoryMarshal.GetReference(green));
            ref var b = ref unsafe(MemoryMarshal.GetReference(blue));
            ref var a = ref unsafe(MemoryMarshal.GetReference(alpha));
            ref var p = ref unsafe(MemoryMarshal.GetReference(pixels));
            var magnitude = Vector128.Create(HalfMagnitude);
            var infinity = Vector128.Create(HalfInfinity);
            var one = Vector128.Create(1f);
            for (; x + Vector128<ushort>.Count <= count; x += Vector128<ushort>.Count)
            {
                var rv = unsafe(Vector128.LoadUnsafe(ref r, (nuint)x));
                var gv = unsafe(Vector128.LoadUnsafe(ref g, (nuint)x));
                var bv = unsafe(Vector128.LoadUnsafe(ref b, (nuint)x));
                var av = unsafe(Vector128.LoadUnsafe(ref a, (nuint)x));
                var largest = Vector128.Max(Vector128.Max(rv & magnitude, gv & magnitude), Vector128.Max(bv & magnitude, av & magnitude));
                if (Vector128.GreaterThanOrEqualAny(largest, infinity))
                {
                    WriteFloatPixelsScalar(red.Slice(x, Vector128<ushort>.Count), green.Slice(x, Vector128<ushort>.Count), blue.Slice(x, Vector128<ushort>.Count), alpha.Slice(x, Vector128<ushort>.Count), pixels.Slice(x * 4, Vector128<ushort>.Count * 4));
                    continue;
                }

                var (r0, r1) = (PixelConverter.HalfToSingle(Vector128.WidenLower(rv)), PixelConverter.HalfToSingle(Vector128.WidenUpper(rv)));
                var (g0, g1) = (PixelConverter.HalfToSingle(Vector128.WidenLower(gv)), PixelConverter.HalfToSingle(Vector128.WidenUpper(gv)));
                var (b0, b1) = (PixelConverter.HalfToSingle(Vector128.WidenLower(bv)), PixelConverter.HalfToSingle(Vector128.WidenUpper(bv)));
                var (a0, a1) = (PixelConverter.HalfToSingle(Vector128.WidenLower(av)), PixelConverter.HalfToSingle(Vector128.WidenUpper(av)));
                if (!Vector128.EqualsAll(a0, one) || !Vector128.EqualsAll(a1, one))
                {
                    // A color of alpha 1 is itself; a color of alpha 0 is kept
                    var keep0 = Vector128.Equals(a0, Vector128<float>.Zero);
                    var keep1 = Vector128.Equals(a1, Vector128<float>.Zero);
                    r0 = Vector128.ConditionalSelect(keep0, r0, r0 / a0);
                    g0 = Vector128.ConditionalSelect(keep0, g0, g0 / a0);
                    b0 = Vector128.ConditionalSelect(keep0, b0, b0 / a0);
                    r1 = Vector128.ConditionalSelect(keep1, r1, r1 / a1);
                    g1 = Vector128.ConditionalSelect(keep1, g1, g1 / a1);
                    b1 = Vector128.ConditionalSelect(keep1, b1, b1 / a1);
                }

                var offset = (nuint)x * 4;
                StorePixels(r0, g0, b0, a0, ref p, offset);
                StorePixels(r1, g1, b1, a1, ref p, offset + 16);
            }
        }

        WriteFloatPixelsScalar(red[x..], green[x..], blue[x..], alpha[x..], pixels[(x * 4)..]);
    }

    /// <summary>The scalar reference of <see cref="WriteFloatPixels"/>.</summary>
    /// <param name="red">The red samples.</param>
    /// <param name="green">The green samples (as many as <paramref name="red"/>).</param>
    /// <param name="blue">The blue samples (as many as <paramref name="red"/>).</param>
    /// <param name="alpha">The alpha samples (as many as <paramref name="red"/>).</param>
    /// <param name="pixels">Receives the pixels, four floats each.</param>
    internal static void WriteFloatPixelsScalar(ReadOnlySpan<ushort> red, ReadOnlySpan<ushort> green, ReadOnlySpan<ushort> blue, ReadOnlySpan<ushort> alpha, Span<float> pixels)
    {
        for (var x = 0; x < red.Length; x++)
        {
            var a = (float)BitConverter.UInt16BitsToHalf(alpha[x]);
            var r = (float)BitConverter.UInt16BitsToHalf(red[x]);
            var g = (float)BitConverter.UInt16BitsToHalf(green[x]);
            var b = (float)BitConverter.UInt16BitsToHalf(blue[x]);
            if (a != 0)
            {
                r = (float)(r / (double)a);
                g = (float)(g / (double)a);
                b = (float)(b / (double)a);
            }

            pixels[(x * 4) + 0] = r;
            pixels[(x * 4) + 1] = g;
            pixels[(x * 4) + 2] = b;
            pixels[(x * 4) + 3] = a;
        }
    }

    /// <summary>
    /// Writes a row of <see cref="RgbaFloat"/> pixels as the HALF sample lines of the channels of an encoded line: the line
    /// holds, for each channel, the samples of its component (0 to 3) for the width of the row. With alpha, a color is
    /// multiplied by the alpha the file stores (the alpha rounded to a half) unless that alpha is 0.
    /// </summary>
    /// <param name="pixels">The pixels, four floats each.</param>
    /// <param name="line">Receives the samples, channel after channel.</param>
    /// <param name="components">The component of each channel.</param>
    /// <param name="hasAlpha">Whether the colors are premultiplied by alpha.</param>
    /// <remarks>
    /// Blocks of 8 pixels whose samples are finite (and whose alphas round to finite halves) are rounded to halves 8 at a
    /// time: the samples themselves when the alphas are 1 or without alpha, otherwise the products of the colors by the
    /// stored alphas, exact in double precision, rounded once. The other blocks take the scalar path.
    /// </remarks>
    public static void WriteHalfSamples(ReadOnlySpan<float> pixels, Span<ushort> line, ReadOnlySpan<int> components, bool hasAlpha)
    {
        var width = pixels.Length / 4;
        _ = line[..(components.Length * width)];
        var x = 0;
        if (UseVectors && components.Length <= 4)
        {
            ref var p = ref unsafe(MemoryMarshal.GetReference(pixels));
            ref var l = ref unsafe(MemoryMarshal.GetReference(line));
            var largest = Vector128.Create(float.MaxValue);
            var one = Vector128.Create(1f);
            var halfInfinity = Vector128.Create((uint)HalfInfinity);
            for (; x + 8 <= width; x += 8)
            {
                var offset = (nuint)x * 4;
                LoadComponents(ref p, offset, out var r0, out var g0, out var b0, out var a0);
                LoadComponents(ref p, offset + 16, out var r1, out var g1, out var b1, out var a1);
                var finite = Vector128.LessThanOrEqual(Vector128.Abs(r0), largest) & Vector128.LessThanOrEqual(Vector128.Abs(r1), largest)
                    & Vector128.LessThanOrEqual(Vector128.Abs(g0), largest) & Vector128.LessThanOrEqual(Vector128.Abs(g1), largest)
                    & Vector128.LessThanOrEqual(Vector128.Abs(b0), largest) & Vector128.LessThanOrEqual(Vector128.Abs(b1), largest);
                finite &= Vector128.LessThanOrEqual(Vector128.Abs(a0), largest) & Vector128.LessThanOrEqual(Vector128.Abs(a1), largest);
                if (finite.AsUInt32() != Vector128<uint>.AllBitsSet)
                {
                    WriteHalfSamplesScalar(pixels, line, components, hasAlpha, x, x + 8);
                    continue;
                }

                var alpha0 = PixelConverter.SingleToHalf(a0);
                var alpha1 = PixelConverter.SingleToHalf(a1);
                Vector128<ushort> red, green, blue;
                if (!hasAlpha || (Vector128.EqualsAll(a0, one) && Vector128.EqualsAll(a1, one)))
                {
                    red = Vector128.Narrow(PixelConverter.SingleToHalf(r0), PixelConverter.SingleToHalf(r1));
                    green = Vector128.Narrow(PixelConverter.SingleToHalf(g0), PixelConverter.SingleToHalf(g1));
                    blue = Vector128.Narrow(PixelConverter.SingleToHalf(b0), PixelConverter.SingleToHalf(b1));
                }
                else
                {
                    // The colors are multiplied by the stored alphas, but by 1 where they are 0
                    if (Vector128.GreaterThanOrEqualAny(alpha0 & Vector128.Create(0x7FFFu), halfInfinity) || Vector128.GreaterThanOrEqualAny(alpha1 & Vector128.Create(0x7FFFu), halfInfinity))
                    {
                        WriteHalfSamplesScalar(pixels, line, components, hasAlpha, x, x + 8);
                        continue;
                    }

                    var stored0 = PixelConverter.HalfToSingle(alpha0);
                    var stored1 = PixelConverter.HalfToSingle(alpha1);
                    var factor0 = Vector128.ConditionalSelect(Vector128.Equals(stored0, Vector128<float>.Zero), one, stored0);
                    var factor1 = Vector128.ConditionalSelect(Vector128.Equals(stored1, Vector128<float>.Zero), one, stored1);
                    red = Vector128.Narrow(MultiplyToHalf(r0, factor0), MultiplyToHalf(r1, factor1));
                    green = Vector128.Narrow(MultiplyToHalf(g0, factor0), MultiplyToHalf(g1, factor1));
                    blue = Vector128.Narrow(MultiplyToHalf(b0, factor0), MultiplyToHalf(b1, factor1));
                }

                for (var c = 0; c < components.Length; c++)
                {
                    var samples = components[c] switch
                    {
                        0 => red,
                        1 => green,
                        2 => blue,
                        _ => Vector128.Narrow(alpha0, alpha1),
                    };
                    unsafe { samples.StoreUnsafe(ref l, (nuint)((c * width) + x)); }
                }
            }
        }

        WriteHalfSamplesScalar(pixels, line, components, hasAlpha, x, width);
    }

    /// <summary>The scalar reference of <see cref="WriteHalfSamples"/>, for the pixels <paramref name="start"/> to <paramref name="end"/> - 1.</summary>
    /// <param name="pixels">The pixels, four floats each.</param>
    /// <param name="line">Receives the samples, channel after channel.</param>
    /// <param name="components">The component of each channel.</param>
    /// <param name="hasAlpha">Whether the colors are premultiplied by alpha.</param>
    /// <param name="start">The first pixel.</param>
    /// <param name="end">The pixel after the last one.</param>
    internal static void WriteHalfSamplesScalar(ReadOnlySpan<float> pixels, Span<ushort> line, ReadOnlySpan<int> components, bool hasAlpha, int start, int end)
    {
        var width = pixels.Length / 4;
        for (var c = 0; c < components.Length; c++)
        {
            var component = components[c];
            for (var x = start; x < end; x++)
            {
                var index = (4 * x) + component;
                double value = pixels[index];
                var alpha = (double)(Half)pixels[index | 3];
                if (hasAlpha && component != 3 && alpha != 0)
                {
                    value *= alpha;
                }

                line[(c * width) + x] = BitConverter.HalfToUInt16Bits((Half)value);
            }
        }
    }

    /// <summary>
    /// Rounds the products of finite floats by finite halves (as floats) to halves (bit patterns): the products are exact in
    /// double precision, and are rounded to nearest, ties to even, beyond the largest half to an infinity. A normal half gets
    /// the rounded fraction, carried into the exponent; a subnormal half is the product added to 2^28, which rounds it to a
    /// multiple of 2^-24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> MultiplyToHalf(Vector128<float> value, Vector128<float> factor)
    {
        var low = RoundToHalf(Vector128.WidenLower(value) * Vector128.WidenLower(factor), out var lowSign);
        var high = RoundToHalf(Vector128.WidenUpper(value) * Vector128.WidenUpper(factor), out var highSign);
        return Vector128.Min(Vector128.Narrow(low, high), Vector128.Create((uint)HalfInfinity)) | Vector128.Narrow(lowSign, highSign);

        static Vector128<ulong> RoundToHalf(Vector128<double> product, out Vector128<ulong> sign)
        {
            var bits = product.AsUInt64();
            sign = Vector128.ShiftRightLogical(bits, 48) & Vector128.Create(0x8000ul);
            var magnitude = bits & Vector128.Create(0x7FFFFFFFFFFFFFFFul);
            var rebased = magnitude - Vector128.Create((1023ul - 15) << 52);
            var rounded = rebased + Vector128.Create((1ul << 41) - 1) + (Vector128.ShiftRightLogical(rebased, 42) & Vector128.Create(1ul));

            // At most 2^22 for a finite product: the clamp to an infinity is done on 32 bits
            var normal = Vector128.ShiftRightLogical(rounded, 42);
            var subnormal = (magnitude.AsDouble() + Vector128.Create(268435456d)).AsUInt64() - Vector128.Create(BitConverter.DoubleToUInt64Bits(268435456d));
            var isSubnormal = Vector128.LessThan(magnitude, Vector128.Create((1023ul - 14) << 52));
            return Vector128.ConditionalSelect(isSubnormal, subnormal, normal);
        }
    }

    /// <summary>Loads four pixels and transposes them to their components.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LoadComponents(ref float source, nuint offset, out Vector128<float> r, out Vector128<float> g, out Vector128<float> b, out Vector128<float> a)
    {
        var p0 = unsafe(Vector128.LoadUnsafe(ref source, offset)).AsUInt32();
        var p1 = unsafe(Vector128.LoadUnsafe(ref source, offset + 4)).AsUInt32();
        var p2 = unsafe(Vector128.LoadUnsafe(ref source, offset + 8)).AsUInt32();
        var p3 = unsafe(Vector128.LoadUnsafe(ref source, offset + 12)).AsUInt32();
        var t0 = ZipLow(p0, p1).AsUInt64();
        var t1 = ZipHigh(p0, p1).AsUInt64();
        var t2 = ZipLow(p2, p3).AsUInt64();
        var t3 = ZipHigh(p2, p3).AsUInt64();
        r = ZipLow(t0, t2).AsSingle();
        g = ZipHigh(t0, t2).AsSingle();
        b = ZipLow(t1, t3).AsSingle();
        a = ZipHigh(t1, t3).AsSingle();
    }

    /// <summary>Stores four pixels given by component: a 4 x 4 transposition.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StorePixels(Vector128<float> r, Vector128<float> g, Vector128<float> b, Vector128<float> a, ref float destination, nuint offset)
    {
        var rgLow = ZipLow(r.AsUInt32(), g.AsUInt32()).AsUInt64();
        var rgHigh = ZipHigh(r.AsUInt32(), g.AsUInt32()).AsUInt64();
        var baLow = ZipLow(b.AsUInt32(), a.AsUInt32()).AsUInt64();
        var baHigh = ZipHigh(b.AsUInt32(), a.AsUInt32()).AsUInt64();
        unsafe { ZipLow(rgLow, baLow).AsSingle().StoreUnsafe(ref destination, offset); }
        unsafe { ZipHigh(rgLow, baLow).AsSingle().StoreUnsafe(ref destination, offset + 4); }
        unsafe { ZipLow(rgHigh, baHigh).AsSingle().StoreUnsafe(ref destination, offset + 8); }
        unsafe { ZipHigh(rgHigh, baHigh).AsSingle().StoreUnsafe(ref destination, offset + 12); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> ZipLow(Vector128<ushort> a, Vector128<ushort> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> ZipHigh(Vector128<ushort> a, Vector128<ushort> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> ZipLow(Vector128<uint> a, Vector128<uint> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> ZipHigh(Vector128<uint> a, Vector128<uint> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> ZipLow(Vector128<ulong> a, Vector128<ulong> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> ZipHigh(Vector128<ulong> a, Vector128<ulong> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);
}
