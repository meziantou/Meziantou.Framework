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
