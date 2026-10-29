using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    private static void ConvertFloatRow<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, Rgba64? background, PixelFormat sourceFormat, PixelFormat destinationFormat, ImageFormat format, FloatSampleConversion floats)
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
        ValidateFloatRow(source, sourceFormat, destinationFormat, background, format, floats);

        var (bgR, bgG, bgB) = background is { } bg ? (bg.R / (double)Max16, bg.G / (double)Max16, bg.B / (double)Max16) : (0d, 0d, 0d);
        ref var src = ref unsafe(MemoryMarshal.GetReference(source));
        ref var dst = ref unsafe(MemoryMarshal.GetReference(destination));
        for (nint i = 0; i < source.Length; i++)
        {
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
    {
        var toInteger = !PixelFormats.IsFloatingPoint(destinationFormat);
        var toHalf = sourceFormat == PixelFormat.RgbaFloat && destinationFormat == PixelFormat.RgbaHalf;
        if (!IsFloat<TSource>() || !(toInteger || toHalf))
            return;

        var removesAlpha = !PixelFormats.HasAlpha(destinationFormat);
        ref var first = ref unsafe(MemoryMarshal.GetReference(source));
        for (var i = 0; i < source.Length; i++)
        {
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
