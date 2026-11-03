using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The conversions from and to the floating-point pixel formats (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>).</summary>
/// <remarks>
/// <para>
/// An integer sample <c>v</c> of largest value <c>max</c> is the fraction <c>v / max</c>: one correctly rounded
/// <see cref="float"/> division for <see cref="RgbaFloat"/>, the nearest <see cref="Half"/> of the exact quotient for
/// <see cref="RgbaHalf"/>. A format without alpha reads as fully opaque (1), and gray is replicated.
/// </para>
/// <para>
/// A floating-point sample becomes an integer one in these steps: a sample that is not a number is rejected; the colors
/// are tone mapped (<see cref="FloatSampleConversion"/>); a sample outside [0, 1] is clipped or rejected; alpha is removed
/// as for the integer formats (flattening <c>c a + bg (1 - a)</c> onto the background, else rejecting non-opaque pixels:
/// an opaque pixel has an alpha of exactly 1); a gray format gets the Rec. 709 luma <c>0.2126 R + 0.7152 G + 0.0722 B</c>
/// (at most 1); and the sample is <c>floor(v max + 1/2)</c>, in double precision. Every <see cref="RgbaFloat"/> of an
/// integer sample goes back to that sample.
/// </para>
/// <para>
/// <see cref="RgbaFloat"/> to <see cref="RgbaHalf"/> rounds to nearest (ties to even), keeps the samples that are not
/// numbers and the infinities, and clips or rejects the finite samples that round beyond the largest half (65,504).
/// <see cref="RgbaHalf"/> to <see cref="RgbaFloat"/> is exact. Every check runs on the whole row before anything is written.
/// </para>
/// </remarks>
internal static partial class PixelConverter
{
    /// <summary>The smallest magnitude of a <see cref="float"/> that rounds to an infinite <see cref="Half"/>: 65,504 plus half of its last step.</summary>
    private const float HalfOverflow = 65520f;

    private const ushort HalfOne = 0x3C00;
    private const ushort HalfNegativeZero = 0x8000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsFloat<TPixel>() => typeof(TPixel) == typeof(RgbaFloat) || typeof(TPixel) == typeof(RgbaHalf);

    /// <summary>Reads the components as fractions: integer samples over their largest value, floating-point samples as they are.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ReadFloat<TPixel>(ref TPixel pixel, out double r, out double g, out double b, out double a)
    {
        if (typeof(TPixel) == typeof(RgbaFloat))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, RgbaFloat>(ref pixel));
            (r, g, b, a) = (p.R, p.G, p.B, p.A);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, RgbaHalf>(ref pixel));
            (r, g, b, a) = ((double)p.R, (double)p.G, (double)p.B, (double)p.A);
        }
        else if (IsPacked<TPixel>())
        {
            ReadPackedComponents(ref pixel, out var ri, out var gi, out var bi, out var ai, out var maxR, out var maxG, out var maxB, out var maxA);
            (r, g, b, a) = (ri / (double)maxR, gi / (double)maxG, bi / (double)maxB, ai / (double)maxA);
        }
        else
        {
            Read(ref pixel, out var ri, out var gi, out var bi, out var ai);
            double max = Is16Bit<TPixel>() ? Max16 : Max8;
            (r, g, b, a) = (ri / max, gi / max, bi / max, ai / max);
        }
    }

    /// <summary>Reads the components of a packed pixel as they are stored, with the largest value of each (a missing alpha is opaque).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadPackedComponents<TPixel>(ref TPixel pixel, out uint r, out uint g, out uint b, out uint a, out uint maxR, out uint maxG, out uint maxB, out uint maxA)
    {
        if (typeof(TPixel) == typeof(Bgr565))
        {
            uint v = unsafe(Unsafe.As<TPixel, Bgr565>(ref pixel)).PackedValue;
            (r, g, b, a) = (v >> 11, (v >> 5) & 63, v & 31, 1);
            (maxR, maxG, maxB, maxA) = (31, 63, 31, 1);
        }
        else if (typeof(TPixel) == typeof(Rgba1010102))
        {
            var v = unsafe(Unsafe.As<TPixel, Rgba1010102>(ref pixel)).PackedValue;
            (r, g, b, a) = (v & 1023, (v >> 10) & 1023, (v >> 20) & 1023, v >> 30);
            (maxR, maxG, maxB, maxA) = (1023, 1023, 1023, 3);
        }
        else
        {
            throw new UnreachableException();
        }
    }

    /// <summary>Converts a fraction in [0, 1] to an integer sample: <c>floor(v max + 1/2)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint ToIntegerSample(double value, uint max) => (uint)Math.Floor((value * max) + 0.5);

    /// <summary>Converts a 16-bit sample to a single-precision fraction: <c>v / 65535</c>, correctly rounded.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float ToFloatSample(ushort value) => value / (float)Max16;

    /// <summary>Gets a value indicating whether a 16-bit color converts to a floating-point format and back to the same color.</summary>
    private static bool IsExactlyRepresentableAsFloat(Rgba64 color, PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
            return true;

        return RoundTripsThroughHalf(color.R) && RoundTripsThroughHalf(color.G) && RoundTripsThroughHalf(color.B) && RoundTripsThroughHalf(color.A);

        static bool RoundTripsThroughHalf(ushort value) => ToIntegerSample((double)(Half)(value / (double)Max16), Max16) == value;
    }

    private static int IndexOfNonOpaqueFloat<TPixel>(ReadOnlySpan<TPixel> row)
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(RgbaFloat))
        {
            var pixels = unsafe(MemoryMarshal.Cast<TPixel, RgbaFloat>(row));
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A != 1f)
                    return i;
            }
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            var pixels = unsafe(MemoryMarshal.Cast<TPixel, RgbaHalf>(row));
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A != Half.One)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>Converts a row of which the source or the destination is a floating-point format (see the remarks of the type).</summary>
    private static void ConvertFloatRow<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, Rgba64? background, PixelFormat sourceFormat, PixelFormat destinationFormat, ImageFormat format, FloatSampleConversion floats, bool useVectors)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        if (!IsFloat<TSource>())
        {
            // Integer samples are fractions of [0, 1]: nothing to check, nothing to flatten (the destination has alpha)
            ConvertIntegerToFloat(source, destination);
            return;
        }

        var toInteger = !IsFloat<TDestination>();
        var toHalf = typeof(TSource) == typeof(RgbaFloat) && typeof(TDestination) == typeof(RgbaHalf);
        var removesAlpha = !PixelFormats.HasAlpha(destinationFormat);

        // Every check runs before anything is written, so that a rejected conversion leaves the destination as it was
        ValidateFloatRowCore(source, sourceFormat, destinationFormat, background, format, floats, useVectors);

        var (bgR, bgG, bgB) = background is { } bg ? (bg.R / (double)Max16, bg.G / (double)Max16, bg.B / (double)Max16) : (0d, 0d, 0d);
        ref var src = ref unsafe(MemoryMarshal.GetReference(source));
        ref var dst = ref unsafe(MemoryMarshal.GetReference(destination));

        // The vector path converts the pixels whose samples are all in [0, 1] to integers, which every policy without tone
        // mapping keeps as they are, and the finite floats that round to finite halves to halves; the scalar loop converts
        // the others one block at a time
        var vectors = useVectors && UseVectors && (toHalf || (toInteger && !floats.ToneMaps && (typeof(TDestination) == typeof(Rgba32) || typeof(TDestination) == typeof(Rgba64))));
        nint end = 0;
        for (nint i = 0; i < source.Length; i++)
        {
            if (vectors && i == end)
            {
                i += toHalf ? ConvertHalfRangePixels(source[(int)i..], destination[(int)i..]) : ConvertUnitPixels(source[(int)i..], destination[(int)i..]);
                end = Math.Min(i + UnitPixelBlock, source.Length);
                if (i >= source.Length)
                    break;
            }

            ReadFloat(ref unsafe(Unsafe.Add(ref src, i)), out var r, out var g, out var b, out var a);
            if (toInteger)
            {
                _ = PrepareInteger(ref r, ref g, ref b, ref a, floats);
                if (removesAlpha && a != 1)
                {
                    r = (r * a) + (bgR * (1 - a));
                    g = (g * a) + (bgG * (1 - a));
                    b = (b * a) + (bgB * (1 - a));
                    a = 1;
                }

                WriteInteger(ref unsafe(Unsafe.Add(ref dst, i)), r, g, b, a);
            }
            else
            {
                if (toHalf)
                {
                    _ = PrepareHalf(ref r, ref g, ref b, ref a, floats.Clips);
                }

                WriteFloat(ref unsafe(Unsafe.Add(ref dst, i)), r, g, b, a);
            }
        }
    }

    /// <summary>
    /// Checks that a row of floating-point pixels converts to a format under a policy: no sample that is not a number, no
    /// sample out of range that the policy does not clip, no non-opaque pixel when alpha is removed without background.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">A pixel cannot be converted.</exception>
    internal static void ValidateFloatRow<TSource>(ReadOnlySpan<TSource> source, PixelFormat sourceFormat, PixelFormat destinationFormat, Rgba64? background, ImageFormat format, FloatSampleConversion floats)
        where TSource : unmanaged
        => ValidateFloatRowCore(source, sourceFormat, destinationFormat, background, format, floats, UseVectors);

    private static void ValidateFloatRowCore<TSource>(ReadOnlySpan<TSource> source, PixelFormat sourceFormat, PixelFormat destinationFormat, Rgba64? background, ImageFormat format, FloatSampleConversion floats, bool useVectors)
        where TSource : unmanaged
    {
        var toInteger = !PixelFormats.IsFloatingPoint(destinationFormat);
        var toHalf = sourceFormat == PixelFormat.RgbaFloat && destinationFormat == PixelFormat.RgbaHalf;
        if (!IsFloat<TSource>() || !(toInteger || toHalf))
            return;

        var removesAlpha = !PixelFormats.HasAlpha(destinationFormat);
        ref var first = ref unsafe(MemoryMarshal.GetReference(source));

        // The vector path skips the pixels the policy accepts as they are: samples in [0, 1] (alpha 1 when it is removed
        // without background) without tone mapping, finite samples that round to finite halves for a half destination
        var vectors = useVectors && UseVectors && (toHalf ? typeof(TSource) == typeof(RgbaFloat) : !floats.ToneMaps);
        var opaque = removesAlpha && background is null;
        var end = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (vectors && i == end)
            {
                i += toHalf ? CountHalfRangePixels(source[i..]) : CountUnitPixels(source[i..], opaque);
                end = Math.Min(i + UnitPixelBlock, source.Length);
                if (i >= source.Length)
                    break;
            }

            ReadFloat(ref unsafe(Unsafe.Add(ref first, i)), out var r, out var g, out var b, out var a);
            var (r0, g0, b0, a0) = (r, g, b, a);
            var status = toInteger ? PrepareInteger(ref r, ref g, ref b, ref a, floats) : PrepareHalf(ref r, ref g, ref b, ref a, floats.Clips);
            switch (status)
            {
                case FloatSampleStatus.NotANumber:
                    throw CreateNotANumberException(sourceFormat, destinationFormat, i, format);
                case FloatSampleStatus.OutOfRange:
                    throw CreateOutOfRangeException(sourceFormat, destinationFormat, i, r0, g0, b0, a0, toInteger, format);
            }

            if (removesAlpha && background is null && a != 1)
                throw CreateNonOpaqueException(sourceFormat, destinationFormat, i, format);
        }
    }

    /// <summary>Checks that a row of raw pixels converts to a format (see <see cref="ValidateFloatRow"/>, and <see cref="IndexOfNonOpaque(PixelFormat, ReadOnlySpan{byte})"/> for the integer formats).</summary>
    /// <exception cref="UnsupportedImageFeatureException">A pixel cannot be converted.</exception>
    internal static void ValidateRow(PixelFormat sourceFormat, ReadOnlySpan<byte> source, PixelFormat destinationFormat, Rgba64? background, ImageFormat format, FloatSampleConversion floats)
    {
        switch (sourceFormat)
        {
            case PixelFormat.RgbaFloat:
                ValidateFloatRow(unsafe(MemoryMarshal.Cast<byte, RgbaFloat>(source)), sourceFormat, destinationFormat, background, format, floats);
                break;
            case PixelFormat.RgbaHalf:
                ValidateFloatRow(unsafe(MemoryMarshal.Cast<byte, RgbaHalf>(source)), sourceFormat, destinationFormat, background, format, floats);
                break;
            default:
                if (PixelFormats.HasAlpha(sourceFormat) && !PixelFormats.HasAlpha(destinationFormat) && background is null && IndexOfNonOpaque(sourceFormat, source) is var index and >= 0)
                    throw CreateNonOpaqueException(sourceFormat, destinationFormat, index, format);

                break;
        }
    }

    /// <summary>Gets the largest finite color component of a row of floating-point pixels (alpha excluded), or 0.</summary>
    internal static double GetLargestFiniteColor(PixelFormat format, ReadOnlySpan<byte> row)
    {
        var largest = 0d;
        if (format == PixelFormat.RgbaFloat)
        {
            foreach (var pixel in unsafe(MemoryMarshal.Cast<byte, RgbaFloat>(row)))
            {
                largest = Largest(largest, pixel.R, pixel.G, pixel.B);
            }
        }
        else if (format == PixelFormat.RgbaHalf)
        {
            foreach (var pixel in unsafe(MemoryMarshal.Cast<byte, RgbaHalf>(row)))
            {
                largest = Largest(largest, (double)pixel.R, (double)pixel.G, (double)pixel.B);
            }
        }

        return largest;

        static double Largest(double largest, double r, double g, double b)
        {
            if (double.IsFinite(r) && r > largest)
                largest = r;

            if (double.IsFinite(g) && g > largest)
                largest = g;

            if (double.IsFinite(b) && b > largest)
                largest = b;

            return largest;
        }
    }

    private static void ConvertIntegerToFloat<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        ref var src = ref unsafe(MemoryMarshal.GetReference(source));
        ref var dst = ref unsafe(MemoryMarshal.GetReference(destination));
        var max = Is16Bit<TSource>() ? Max16 : Max8;
        for (nint i = 0; i < source.Length; i++)
        {
            // A packed component is the fraction of its own largest value, not of its 16-bit expansion
            uint r, g, b, a, maxR, maxG, maxB, maxA;
            if (IsPacked<TSource>())
            {
                ReadPackedComponents(ref unsafe(Unsafe.Add(ref src, i)), out r, out g, out b, out a, out maxR, out maxG, out maxB, out maxA);
            }
            else
            {
                Read(ref unsafe(Unsafe.Add(ref src, i)), out r, out g, out b, out a);
                (maxR, maxG, maxB, maxA) = (max, max, max, max);
            }

            if (typeof(TDestination) == typeof(RgbaFloat))
            {
                // One correctly rounded division: the integers are exact in single precision
                unsafe { Unsafe.As<TDestination, RgbaFloat>(ref Unsafe.Add(ref dst, i)) = new RgbaFloat(r / (float)maxR, g / (float)maxG, b / (float)maxB, a / (float)maxA); }
            }
            else
            {
                unsafe { Unsafe.As<TDestination, RgbaHalf>(ref Unsafe.Add(ref dst, i)) = new RgbaHalf((Half)(r / (double)maxR), (Half)(g / (double)maxG), (Half)(b / (double)maxB), (Half)(a / (double)maxA)); }
            }
        }
    }

    /// <summary>The number of pixels the vector paths of the floating-point conversions take at a time.</summary>
    private const int UnitPixelBlock = 4;

    /// <summary>
    /// Counts the leading pixels, by blocks of <see cref="UnitPixelBlock"/>, whose samples are all in [0, 1] (not a number
    /// excluded), with an alpha of exactly 1 when <paramref name="opaque"/> is set.
    /// </summary>
    private static int CountUnitPixels<TSource>(ReadOnlySpan<TSource> source, bool opaque)
        where TSource : unmanaged
    {
        var count = 0;
        if (typeof(TSource) == typeof(RgbaFloat))
        {
            ref var samples = ref unsafe(Unsafe.As<TSource, float>(ref MemoryMarshal.GetReference(source)));
            var low = Vector128.Create(0f, 0f, 0f, opaque ? 1f : 0f);
            var high = Vector128.Create(1f);
            for (; count + UnitPixelBlock <= source.Length; count += UnitPixelBlock)
            {
                var offset = (nuint)count * 4;
                var v0 = unsafe(Vector128.LoadUnsafe(ref samples, offset));
                var v1 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 4));
                var v2 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 8));
                var v3 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 12));
                var valid = Vector128.GreaterThanOrEqual(v0, low) & Vector128.LessThanOrEqual(v0, high)
                    & Vector128.GreaterThanOrEqual(v1, low) & Vector128.LessThanOrEqual(v1, high)
                    & Vector128.GreaterThanOrEqual(v2, low) & Vector128.LessThanOrEqual(v2, high)
                    & Vector128.GreaterThanOrEqual(v3, low) & Vector128.LessThanOrEqual(v3, high);
                if (valid.AsUInt32() != Vector128<uint>.AllBitsSet)
                    break;
            }
        }
        else if (typeof(TSource) == typeof(RgbaHalf))
        {
            // On the bits: [0, 1] is 0 to 0x3C00, and -0 (0x8000)
            ref var samples = ref unsafe(Unsafe.As<TSource, ushort>(ref MemoryMarshal.GetReference(source)));
            var alphaLow = opaque ? HalfOne : (ushort)0;
            var alphaNegativeZero = opaque ? HalfOne : HalfNegativeZero;
            var low = Vector128.Create(0, 0, 0, alphaLow, 0, 0, 0, alphaLow);
            var high = Vector128.Create(HalfOne);
            var negativeZero = Vector128.Create(HalfNegativeZero, HalfNegativeZero, HalfNegativeZero, alphaNegativeZero, HalfNegativeZero, HalfNegativeZero, HalfNegativeZero, alphaNegativeZero);
            for (; count + UnitPixelBlock <= source.Length; count += UnitPixelBlock)
            {
                var offset = (nuint)count * 4;
                var h0 = unsafe(Vector128.LoadUnsafe(ref samples, offset));
                var h1 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 8));
                var valid = ((Vector128.GreaterThanOrEqual(h0, low) & Vector128.LessThanOrEqual(h0, high)) | Vector128.Equals(h0, negativeZero))
                    & ((Vector128.GreaterThanOrEqual(h1, low) & Vector128.LessThanOrEqual(h1, high)) | Vector128.Equals(h1, negativeZero));
                if (valid != Vector128<ushort>.AllBitsSet)
                    break;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts the leading <see cref="RgbaFloat"/> pixels, by blocks of <see cref="UnitPixelBlock"/>, whose samples are all
    /// finite and round to finite halves.
    /// </summary>
    private static int CountHalfRangePixels<TSource>(ReadOnlySpan<TSource> source)
        where TSource : unmanaged
    {
        var count = 0;
        if (typeof(TSource) == typeof(RgbaFloat))
        {
            ref var samples = ref unsafe(Unsafe.As<TSource, float>(ref MemoryMarshal.GetReference(source)));
            var high = Vector128.Create(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(HalfOverflow) - 1));
            var low = -high;
            for (; count + UnitPixelBlock <= source.Length; count += UnitPixelBlock)
            {
                var offset = (nuint)count * 4;
                var v0 = unsafe(Vector128.LoadUnsafe(ref samples, offset));
                var v1 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 4));
                var v2 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 8));
                var v3 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 12));
                var valid = Vector128.GreaterThanOrEqual(v0, low) & Vector128.LessThanOrEqual(v0, high)
                    & Vector128.GreaterThanOrEqual(v1, low) & Vector128.LessThanOrEqual(v1, high)
                    & Vector128.GreaterThanOrEqual(v2, low) & Vector128.LessThanOrEqual(v2, high)
                    & Vector128.GreaterThanOrEqual(v3, low) & Vector128.LessThanOrEqual(v3, high);
                if (valid.AsUInt32() != Vector128<uint>.AllBitsSet)
                    break;
            }
        }

        return count;
    }

    /// <summary>
    /// Converts the leading <see cref="RgbaFloat"/> pixels, by blocks of <see cref="UnitPixelBlock"/>, whose samples are all
    /// finite and round to finite halves, to <see cref="RgbaHalf"/> (<see cref="SingleToHalf"/>, the rounding of a cast).
    /// </summary>
    /// <returns>The number of pixels converted.</returns>
    private static int ConvertHalfRangePixels<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        var count = 0;
        if (typeof(TSource) != typeof(RgbaFloat) || typeof(TDestination) != typeof(RgbaHalf))
            return count;

        ref var samples = ref unsafe(Unsafe.As<TSource, float>(ref MemoryMarshal.GetReference(source)));
        ref var output = ref unsafe(Unsafe.As<TDestination, ushort>(ref MemoryMarshal.GetReference(destination)));
        var high = Vector128.Create(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(HalfOverflow) - 1));
        var low = -high;
        for (; count + UnitPixelBlock <= source.Length; count += UnitPixelBlock)
        {
            var offset = (nuint)count * 4;
            var v0 = unsafe(Vector128.LoadUnsafe(ref samples, offset));
            var v1 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 4));
            var v2 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 8));
            var v3 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 12));
            var valid = Vector128.GreaterThanOrEqual(v0, low) & Vector128.LessThanOrEqual(v0, high)
                & Vector128.GreaterThanOrEqual(v1, low) & Vector128.LessThanOrEqual(v1, high)
                & Vector128.GreaterThanOrEqual(v2, low) & Vector128.LessThanOrEqual(v2, high)
                & Vector128.GreaterThanOrEqual(v3, low) & Vector128.LessThanOrEqual(v3, high);
            if (valid.AsUInt32() != Vector128<uint>.AllBitsSet)
                break;

            unsafe { Vector128.Narrow(SingleToHalf(v0), SingleToHalf(v1)).StoreUnsafe(ref output, offset); }
            unsafe { Vector128.Narrow(SingleToHalf(v2), SingleToHalf(v3)).StoreUnsafe(ref output, offset + 8); }
        }

        return count;
    }

    /// <summary>
    /// Converts the leading <see cref="RgbaFloat"/> or <see cref="RgbaHalf"/> pixels, by blocks of
    /// <see cref="UnitPixelBlock"/>, whose samples are all in [0, 1], to <see cref="Rgba32"/> or <see cref="Rgba64"/>:
    /// <c>floor(v max + 1/2)</c> in double precision, exact for a float (or a half) times 255 or 65,535.
    /// </summary>
    /// <returns>The number of pixels converted.</returns>
    private static int ConvertUnitPixels<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        var count = 0;
        var max = Vector128.Create(typeof(TDestination) == typeof(Rgba32) ? (double)Max8 : Max16);
        var low = Vector128<float>.Zero;
        var high = Vector128.Create(1f);
        ref var output = ref unsafe(Unsafe.As<TDestination, byte>(ref MemoryMarshal.GetReference(destination)));
        for (; count + UnitPixelBlock <= source.Length; count += UnitPixelBlock)
        {
            Vector128<float> v0, v1, v2, v3;
            var offset = (nuint)count * 4;
            if (typeof(TSource) == typeof(RgbaFloat))
            {
                ref var samples = ref unsafe(Unsafe.As<TSource, float>(ref MemoryMarshal.GetReference(source)));
                v0 = unsafe(Vector128.LoadUnsafe(ref samples, offset));
                v1 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 4));
                v2 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 8));
                v3 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 12));
            }
            else
            {
                // A sample that is infinite or not a number reads as at least 65,536, out of [0, 1]
                ref var samples = ref unsafe(Unsafe.As<TSource, ushort>(ref MemoryMarshal.GetReference(source)));
                var h0 = unsafe(Vector128.LoadUnsafe(ref samples, offset));
                var h1 = unsafe(Vector128.LoadUnsafe(ref samples, offset + 8));
                v0 = HalfToSingle(Vector128.WidenLower(h0));
                v1 = HalfToSingle(Vector128.WidenUpper(h0));
                v2 = HalfToSingle(Vector128.WidenLower(h1));
                v3 = HalfToSingle(Vector128.WidenUpper(h1));
            }

            var valid = Vector128.GreaterThanOrEqual(v0, low) & Vector128.LessThanOrEqual(v0, high)
                & Vector128.GreaterThanOrEqual(v1, low) & Vector128.LessThanOrEqual(v1, high)
                & Vector128.GreaterThanOrEqual(v2, low) & Vector128.LessThanOrEqual(v2, high)
                & Vector128.GreaterThanOrEqual(v3, low) & Vector128.LessThanOrEqual(v3, high);
            if (valid.AsUInt32() != Vector128<uint>.AllBitsSet)
                break;

            var q01 = Vector128.Narrow(Quantize(v0, max), Quantize(v1, max));
            var q23 = Vector128.Narrow(Quantize(v2, max), Quantize(v3, max));
            if (typeof(TDestination) == typeof(Rgba32))
            {
                unsafe { Vector128.Narrow(q01, q23).StoreUnsafe(ref output, (nuint)count * 4); }
            }
            else
            {
                unsafe { q01.AsByte().StoreUnsafe(ref output, (nuint)count * 8); }
                unsafe { q23.AsByte().StoreUnsafe(ref output, ((nuint)count * 8) + 16); }
            }
        }

        return count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<uint> Quantize(Vector128<float> value, Vector128<double> max)
        {
            var half = Vector128.Create(0.5);
            var low = Vector128.Floor((Vector128.WidenLower(value) * max) + half);
            var high = Vector128.Floor((Vector128.WidenUpper(value) * max) + half);
            return Vector128.ConvertToInt32(Vector128.Narrow(low, high)).AsUInt32();
        }
    }

    /// <summary>
    /// Converts finite halves (zero-extended bit patterns) to floats exactly: a normal half has its exponent rebased and
    /// its fraction moved, a subnormal half is its fraction times 2^-24, and the sign is copied. An infinity or a value
    /// that is not a number gives a finite float of at least 65,536 in magnitude.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<float> HalfToSingle(Vector128<uint> half)
    {
        var magnitude = half & Vector128.Create(0x7FFFu);
        var sign = Vector128.ShiftLeft(half & Vector128.Create(0x8000u), 16);
        var normal = Vector128.ShiftLeft(magnitude, 13) + Vector128.Create((127u - 15u) << 23);
        var subnormal = Vector128.ConvertToSingle(magnitude.AsInt32()) * Vector128.Create(1f / (1 << 24));
        var isSubnormal = Vector128.LessThan(magnitude, Vector128.Create(0x0400u));
        return Vector128.ConditionalSelect(isSubnormal.AsSingle(), subnormal, normal.AsSingle()) | sign.AsSingle();
    }

    /// <summary>
    /// Rounds finite floats to halves (bit patterns, to nearest, ties to even, beyond the largest half to an infinity): a
    /// normal half gets the rounded fraction, carried into the exponent; a subnormal half is the float added to 1/2, which
    /// rounds it to a multiple of 2^-24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<uint> SingleToHalf(Vector128<float> value)
    {
        var bits = value.AsUInt32();
        var sign = Vector128.ShiftRightLogical(bits, 16) & Vector128.Create(0x8000u);
        var magnitude = bits & Vector128.Create(0x7FFFFFFFu);
        var rebased = magnitude - Vector128.Create((127u - 15u) << 23);
        var rounded = rebased + Vector128.Create(0x0FFFu) + (Vector128.ShiftRightLogical(rebased, 13) & Vector128.Create(1u));
        var normal = Vector128.Min(Vector128.ShiftRightLogical(rounded, 13), Vector128.Create(0x7C00u));
        var subnormal = (magnitude.AsSingle() + Vector128.Create(0.5f)).AsUInt32() - Vector128.Create(0x3F000000u);
        var isSubnormal = Vector128.LessThan(magnitude, Vector128.Create((127u - 14u) << 23));
        return Vector128.ConditionalSelect(isSubnormal, subnormal, normal) | sign;
    }

    /// <summary>Brings the components of a floating-point pixel into [0, 1]: tone mapping, then clipping when the policy allows it.</summary>
    private static FloatSampleStatus PrepareInteger(ref double r, ref double g, ref double b, ref double a, FloatSampleConversion floats)
    {
        if (double.IsNaN(r) || double.IsNaN(g) || double.IsNaN(b) || double.IsNaN(a))
            return FloatSampleStatus.NotANumber;

        floats.ToneMap(ref r, ref g, ref b);
        if (IsInUnitRange(r) && IsInUnitRange(g) && IsInUnitRange(b) && IsInUnitRange(a))
            return FloatSampleStatus.Valid;

        if (!floats.Clips)
            return FloatSampleStatus.OutOfRange;

        r = Math.Clamp(r, 0, 1);
        g = Math.Clamp(g, 0, 1);
        b = Math.Clamp(b, 0, 1);
        a = Math.Clamp(a, 0, 1);
        return FloatSampleStatus.Valid;
    }

    /// <summary>Clips the finite components that round beyond the range of <see cref="Half"/>, when the policy allows it.</summary>
    private static FloatSampleStatus PrepareHalf(ref double r, ref double g, ref double b, ref double a, bool clips)
    {
        if (!OverflowsHalf(r) && !OverflowsHalf(g) && !OverflowsHalf(b) && !OverflowsHalf(a))
            return FloatSampleStatus.Valid;

        if (!clips)
            return FloatSampleStatus.OutOfRange;

        r = ClipToHalf(r);
        g = ClipToHalf(g);
        b = ClipToHalf(b);
        a = ClipToHalf(a);
        return FloatSampleStatus.Valid;

        static double ClipToHalf(double value) => OverflowsHalf(value) ? Math.CopySign((double)Half.MaxValue, value) : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInUnitRange(double value) => value is >= 0 and <= 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool OverflowsHalf(double value) => double.IsFinite(value) && Math.Abs(value) >= HalfOverflow;

    /// <summary>Writes components in [0, 1] to an integer pixel: luma for gray, then <c>floor(v max + 1/2)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteInteger<TPixel>(ref TPixel pixel, double r, double g, double b, double a)
    {
        if (IsGray<TPixel>())
        {
            var y = Math.Min((0.2126 * r) + (0.7152 * g) + (0.0722 * b), 1);
            if (typeof(TPixel) == typeof(Gray8))
            {
                unsafe { Unsafe.As<TPixel, Gray8>(ref pixel).Value = (byte)ToIntegerSample(y, Max8); }
            }
            else if (typeof(TPixel) == typeof(Gray16))
            {
                unsafe { Unsafe.As<TPixel, Gray16>(ref pixel).Value = (ushort)ToIntegerSample(y, Max16); }
            }
            else if (typeof(TPixel) == typeof(La16))
            {
                ref var p = ref unsafe(Unsafe.As<TPixel, La16>(ref pixel));
                (p.L, p.A) = ((byte)ToIntegerSample(y, Max8), (byte)ToIntegerSample(a, Max8));
            }
            else
            {
                ref var p = ref unsafe(Unsafe.As<TPixel, La32>(ref pixel));
                (p.L, p.A) = ((ushort)ToIntegerSample(y, Max16), (ushort)ToIntegerSample(a, Max16));
            }
        }
        else if (typeof(TPixel) == typeof(A8))
        {
            unsafe { Unsafe.As<TPixel, A8>(ref pixel).A = (byte)ToIntegerSample(a, Max8); }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, Rgba64>(ref pixel));
            (p.R, p.G, p.B, p.A) = ((ushort)ToIntegerSample(r, Max16), (ushort)ToIntegerSample(g, Max16), (ushort)ToIntegerSample(b, Max16), (ushort)ToIntegerSample(a, Max16));
        }
        else if (typeof(TPixel) == typeof(Rgb48))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, Rgb48>(ref pixel));
            (p.R, p.G, p.B) = ((ushort)ToIntegerSample(r, Max16), (ushort)ToIntegerSample(g, Max16), (ushort)ToIntegerSample(b, Max16));
        }
        else if (typeof(TPixel) == typeof(Bgr565))
        {
            unsafe { Unsafe.As<TPixel, Bgr565>(ref pixel).PackedValue = (ushort)((ToIntegerSample(r, 31) << 11) | (ToIntegerSample(g, 63) << 5) | ToIntegerSample(b, 31)); }
        }
        else if (typeof(TPixel) == typeof(Rgba1010102))
        {
            unsafe { Unsafe.As<TPixel, Rgba1010102>(ref pixel).PackedValue = ToIntegerSample(r, 1023) | (ToIntegerSample(g, 1023) << 10) | (ToIntegerSample(b, 1023) << 20) | (ToIntegerSample(a, 3) << 30); }
        }
        else
        {
            var (r8, g8, b8, a8) = ((byte)ToIntegerSample(r, Max8), (byte)ToIntegerSample(g, Max8), (byte)ToIntegerSample(b, Max8), (byte)ToIntegerSample(a, Max8));
            if (typeof(TPixel) == typeof(Rgba32))
            {
                ref var p = ref unsafe(Unsafe.As<TPixel, Rgba32>(ref pixel));
                (p.R, p.G, p.B, p.A) = (r8, g8, b8, a8);
            }
            else if (typeof(TPixel) == typeof(Bgra32))
            {
                ref var p = ref unsafe(Unsafe.As<TPixel, Bgra32>(ref pixel));
                (p.B, p.G, p.R, p.A) = (b8, g8, r8, a8);
            }
            else if (typeof(TPixel) == typeof(Argb32))
            {
                ref var p = ref unsafe(Unsafe.As<TPixel, Argb32>(ref pixel));
                (p.A, p.R, p.G, p.B) = (a8, r8, g8, b8);
            }
            else if (typeof(TPixel) == typeof(Abgr32))
            {
                ref var p = ref unsafe(Unsafe.As<TPixel, Abgr32>(ref pixel));
                (p.A, p.B, p.G, p.R) = (a8, b8, g8, r8);
            }
            else
            {
                ref var p = ref unsafe(Unsafe.As<TPixel, Rgb24>(ref pixel));
                (p.R, p.G, p.B) = (r8, g8, b8);
            }
        }
    }

    /// <summary>Writes components to a floating-point pixel, rounded to nearest.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFloat<TPixel>(ref TPixel pixel, double r, double g, double b, double a)
    {
        if (typeof(TPixel) == typeof(RgbaFloat))
        {
            unsafe { Unsafe.As<TPixel, RgbaFloat>(ref pixel) = new RgbaFloat((float)r, (float)g, (float)b, (float)a); }
        }
        else
        {
            unsafe { Unsafe.As<TPixel, RgbaHalf>(ref pixel) = new RgbaHalf((Half)r, (Half)g, (Half)b, (Half)a); }
        }
    }

    internal static UnsupportedImageFeatureException CreateNotANumberException(PixelFormat sourceFormat, PixelFormat destinationFormat, int index, ImageFormat format = ImageFormat.Unknown)
        => new(string.Create(CultureInfo.InvariantCulture, $"Converting {sourceFormat} pixels to {destinationFormat} would have to give a value to a sample that is not a number: the pixel at index {index} of the row has one. No option converts such a sample; replace it first."), format, "Not-a-number samples");

    internal static UnsupportedImageFeatureException CreateOutOfRangeException(PixelFormat sourceFormat, PixelFormat destinationFormat, int index, double r, double g, double b, double a, bool toInteger, ImageFormat format = ImageFormat.Unknown)
    {
        var range = toInteger ? "outside [0, 1]" : "beyond the range of Half (65,504)";
        var remedy = toInteger
            ? "Set PixelConversionOptions.OutOfRangeSamples to Clip, or PixelConversionOptions.ToneMapping to compress the highlights"
            : "Set PixelConversionOptions.OutOfRangeSamples to Clip";
        return new(string.Create(CultureInfo.InvariantCulture, $"Converting {sourceFormat} pixels to {destinationFormat} would clip the samples {range}: the pixel at index {index} of the row is ({r}, {g}, {b}, {a}). {remedy}."), format, "Out-of-range samples");
    }

    private enum FloatSampleStatus
    {
        Valid,
        NotANumber,
        OutOfRange,
    }
}
