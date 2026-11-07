using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The double-precision working samples of the filtering operations: conversion of stored
/// samples to working samples and back, shared by <see cref="Resampler"/> and <see cref="Convolver"/> so that both follow
/// the same premultiplication, transfer function, rounding and clamping rules.
/// </summary>
/// <remarks>
/// The stored samples are <see cref="byte"/> and <see cref="ushort"/> for the integer formats, <see cref="float"/>
/// (<see cref="RgbaFloat"/>) and <see cref="Half"/> (<see cref="RgbaHalf"/>) for the floating-point ones, whose working
/// value is the sample itself (a maximum of 1). Floating-point colors are stored rounded to the nearest
/// <see cref="float"/> or <see cref="Half"/> (a finite value beyond the largest half becomes an infinity), without
/// clamping, so that values above 1 and below 0 survive the filters; alpha is a coverage, clamped to [0, 1]. A sample
/// that is not a number is not treated apart: it propagates through the arithmetic to the samples that read it.
/// </remarks>
internal static class WorkingSamples
{
    /// <summary>The rounding offset: one half plus the tie bias <c>2^-20</c> (about 9.5e-7 sample units), exactly representable.</summary>
    internal const double RoundingOffset = 0.5 + (1.0 / (1 << 20));

    // The thresholds of the encoding of linear light to 8-bit samples, by code point of the transfer characteristics
    private static readonly QuantizationThresholds?[] Thresholds8 = new QuantizationThresholds?[19];

    /// <summary>
    /// Converts stored samples to working samples: the encoded value <c>v</c> (encoded) or <c>Decode(v / max)</c> (linear
    /// light, with the function of the transfer characteristics <paramref name="transfer"/> of <see cref="CicpTransfer"/>;
    /// 0 keeps the encoded values). With <see cref="WorkingAlpha.Premultiplied"/>, colors are multiplied by the alpha sample <c>a</c> (0 to max);
    /// with <see cref="WorkingAlpha.Preserved"/> they are kept straight. In both cases the alpha working sample is <c>a</c>.
    /// With <see cref="WorkingAlpha.Opaque"/>, the working samples are the three colors of each pixel, straight: 3 per 4
    /// stored samples.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, encoded samples are converted 4 at a time (one 256-bit vector with AVX2, except the
    /// colors of opaque pixels): every sample and product is an integer below 2^32, so the conversions and the products are
    /// exact and give the doubles of <see cref="LoadScalar"/>.
    /// </remarks>
    public static void Load<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha, byte transfer)
        where TSample : unmanaged
    {
        var done = 0;
        if (transfer is 0 && !IsFloatingPoint<TSample>() && !Resampler.TestForceScalar)
        {
            if (Vector256.IsHardwareAccelerated && Avx2.IsSupported && alpha != WorkingAlpha.Opaque)
            {
                done = LoadVector256(samples, working, alpha);
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                done = LoadVector(samples, working, alpha);
            }
        }

        LoadScalar(samples[done..], working[GetWorkingLength(done, alpha)..], alpha, transfer);
    }

    /// <summary>Gets the largest stored sample of a pixel format: 255 or 65,535 for the integer formats, 1 for the floating-point ones.</summary>
    internal static int GetMaxValue(PixelFormat format)
        => PixelFormats.IsFloatingPoint(format) ? 1 : PixelFormats.GetBitsPerComponent(format) == 16 ? ushort.MaxValue : byte.MaxValue;

    /// <summary>Gets a value indicating whether the stored samples are floating-point numbers (<see cref="float"/> or <see cref="Half"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsFloatingPoint<TSample>()
        => typeof(TSample) == typeof(float) || typeof(TSample) == typeof(Half);

    /// <summary>Gets the number of working samples of <paramref name="count"/> stored samples (3 per 4 for <see cref="WorkingAlpha.Opaque"/>).</summary>
    internal static int GetWorkingLength(int count, WorkingAlpha alpha) => alpha == WorkingAlpha.Opaque ? count / 4 * 3 : count;

    /// <summary>The scalar reference of <see cref="Load"/>.</summary>
    internal static void LoadScalar<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha, byte transfer)
        where TSample : unmanaged
    {
        if (IsFloatingPoint<TSample>())
        {
            LoadFloatingPoint(samples, working, alpha, transfer);
            return;
        }

        // The tables of the sRGB function are those of SrgbTransfer, whatever the way sRGB is declared
        var linear = transfer is not 0;
        var decode = !linear ? default : typeof(TSample) == typeof(byte) ? CicpTransfer.GetDecode8(transfer) : CicpTransfer.GetDecode16(transfer);
        if (alpha == WorkingAlpha.Premultiplied)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                double a = ToInt(samples[i + 3]);
                if (linear)
                {
                    working[i] = decode[ToInt(samples[i])] * a;
                    working[i + 1] = decode[ToInt(samples[i + 1])] * a;
                    working[i + 2] = decode[ToInt(samples[i + 2])] * a;
                }
                else
                {
                    working[i] = ToInt(samples[i]) * a;
                    working[i + 1] = ToInt(samples[i + 1]) * a;
                    working[i + 2] = ToInt(samples[i + 2]) * a;
                }

                working[i + 3] = a;
            }
        }
        else if (alpha == WorkingAlpha.Preserved)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                if (linear)
                {
                    working[i] = decode[ToInt(samples[i])];
                    working[i + 1] = decode[ToInt(samples[i + 1])];
                    working[i + 2] = decode[ToInt(samples[i + 2])];
                }
                else
                {
                    working[i] = ToInt(samples[i]);
                    working[i + 1] = ToInt(samples[i + 1]);
                    working[i + 2] = ToInt(samples[i + 2]);
                }

                working[i + 3] = ToInt(samples[i + 3]);
            }
        }
        else if (alpha == WorkingAlpha.Opaque)
        {
            for (int i = 0, o = 0; i < samples.Length; i += 4, o += 3)
            {
                if (linear)
                {
                    working[o] = decode[ToInt(samples[i])];
                    working[o + 1] = decode[ToInt(samples[i + 1])];
                    working[o + 2] = decode[ToInt(samples[i + 2])];
                }
                else
                {
                    working[o] = ToInt(samples[i]);
                    working[o + 1] = ToInt(samples[i + 1]);
                    working[o + 2] = ToInt(samples[i + 2]);
                }
            }
        }
        else if (linear)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                working[i] = decode[ToInt(samples[i])];
            }
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
            {
                working[i] = ToInt(samples[i]);
            }
        }
    }

    /// <summary>
    /// Converts filtered working samples to stored samples. With <see cref="WorkingAlpha.Premultiplied"/>: <c>A</c> is
    /// rounded to the output alpha; zero gives transparent black, otherwise each color is unpremultiplied by the unrounded
    /// <c>A</c>. With <see cref="WorkingAlpha.Preserved"/>, the stored alpha sample is left untouched. With
    /// <see cref="WorkingAlpha.Opaque"/>, the values are the three colors of each pixel and the stored alpha is
    /// <paramref name="max"/>. Colors are then encoded (linear light: clamped to [0, 1] and <c>max * Encode(L)</c>), rounded
    /// with <c>floor(v + 0.5 + 2^-20)</c> and clamped to [0, max].
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, encoded samples without alpha, with premultiplied alpha or of opaque pixels are rounded
    /// 4 at a time (one 256-bit vector with AVX2, except opaque pixels) with the same IEEE operations (division, addition,
    /// floor) as <see cref="StoreScalar"/>, so the results are identical. Linear light is encoded to 8-bit samples with the
    /// thresholds of the encoding (<see cref="QuantizationThresholds"/>), which give the samples of
    /// <see cref="StoreScalar"/> without evaluating the transfer function.
    /// </remarks>
    public static void Store<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, WorkingAlpha alpha, byte transfer, int max)
        where TSample : unmanaged
    {
        var done = 0;
        if (transfer is 0 && alpha != WorkingAlpha.Preserved && !IsFloatingPoint<TSample>() && !Resampler.TestForceScalar)
        {
            if (alpha == WorkingAlpha.Opaque)
            {
                if (Vector128.IsHardwareAccelerated)
                {
                    done = StoreOpaqueVector(values, samples, max);
                }
            }
            else if (Vector256.IsHardwareAccelerated && Avx2.IsSupported)
            {
                done = StoreVector256(values, samples, alpha == WorkingAlpha.Premultiplied, max);
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                done = StoreVector(values, samples, alpha == WorkingAlpha.Premultiplied, max);
            }
        }

        var thresholds = typeof(TSample) == typeof(byte) && max == byte.MaxValue && transfer is not 0 and not CicpTransfer.Linear && transfer < Thresholds8.Length && !Resampler.TestForceScalar ? GetThresholds8(transfer) : null;
        StoreCore(values[GetWorkingLength(done, alpha)..], samples[done..], alpha, transfer, max, thresholds);
    }

    /// <summary>The scalar reference of <see cref="Store"/>.</summary>
    internal static void StoreScalar<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, WorkingAlpha alpha, byte transfer, int max)
        where TSample : unmanaged
        => StoreCore(values, samples, alpha, transfer, max, thresholds: null);

    /// <summary>Gets the thresholds of the encoding of linear light to 8-bit samples (<see cref="EncodeColor"/>), or <see langword="null"/> when they do not give its samples.</summary>
    internal static QuantizationThresholds? GetThresholds8(byte transfer)
        => QuantizationThresholds.GetOrCreate(Thresholds8, transfer, value => EncodeColor(value, byte.MaxValue, transfer, thresholds: null), byte.MaxValue);

    private static void StoreCore<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, WorkingAlpha alpha, byte transfer, int max, QuantizationThresholds? thresholds)
        where TSample : unmanaged
    {
        Debug.Assert(GetWorkingLength(samples.Length, alpha) == values.Length);
        if (IsFloatingPoint<TSample>())
        {
            StoreFloatingPoint(values, samples, alpha, transfer);
            return;
        }

        if (alpha == WorkingAlpha.Opaque)
        {
            for (int i = 0, o = 0; i < samples.Length; i += 4, o += 3)
            {
                samples[i] = FromInt<TSample>(EncodeColor(values[o], max, transfer, thresholds));
                samples[i + 1] = FromInt<TSample>(EncodeColor(values[o + 1], max, transfer, thresholds));
                samples[i + 2] = FromInt<TSample>(EncodeColor(values[o + 2], max, transfer, thresholds));
                samples[i + 3] = FromInt<TSample>(max);
            }
        }
        else if (alpha == WorkingAlpha.Premultiplied)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                var a = values[i + 3];
                var roundedAlpha = Round(a, max);
                if (roundedAlpha == 0)
                {
                    samples.Slice(i, 4).Clear();
                    continue;
                }

                samples[i] = FromInt<TSample>(EncodeColor(values[i] / a, max, transfer, thresholds));
                samples[i + 1] = FromInt<TSample>(EncodeColor(values[i + 1] / a, max, transfer, thresholds));
                samples[i + 2] = FromInt<TSample>(EncodeColor(values[i + 2] / a, max, transfer, thresholds));
                samples[i + 3] = FromInt<TSample>(roundedAlpha);
            }
        }
        else if (alpha == WorkingAlpha.Preserved)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                samples[i] = FromInt<TSample>(EncodeColor(values[i], max, transfer, thresholds));
                samples[i + 1] = FromInt<TSample>(EncodeColor(values[i + 1], max, transfer, thresholds));
                samples[i + 2] = FromInt<TSample>(EncodeColor(values[i + 2], max, transfer, thresholds));
            }
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = FromInt<TSample>(EncodeColor(values[i], max, transfer, thresholds));
            }
        }
    }

    /// <summary>
    /// The <see cref="LoadScalar"/> of floating-point samples: the working value of a sample <c>v</c> is <c>v</c>
    /// (encoded) or <c>Decode(v)</c> in double precision (linear light: the caller has checked that every color is a
    /// number in [0, 1], the domain of the function), premultiplied by the alpha sample as for the integer formats.
    /// </summary>
    private static void LoadFloatingPoint<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha, byte transfer)
        where TSample : unmanaged
    {
        if (alpha == WorkingAlpha.Premultiplied)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                var a = ToDouble(samples[i + 3]);
                working[i] = DecodeColor(ToDouble(samples[i]), transfer) * a;
                working[i + 1] = DecodeColor(ToDouble(samples[i + 1]), transfer) * a;
                working[i + 2] = DecodeColor(ToDouble(samples[i + 2]), transfer) * a;
                working[i + 3] = a;
            }
        }
        else if (alpha == WorkingAlpha.Preserved)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                working[i] = DecodeColor(ToDouble(samples[i]), transfer);
                working[i + 1] = DecodeColor(ToDouble(samples[i + 1]), transfer);
                working[i + 2] = DecodeColor(ToDouble(samples[i + 2]), transfer);
                working[i + 3] = ToDouble(samples[i + 3]);
            }
        }
        else if (alpha == WorkingAlpha.Opaque)
        {
            for (int i = 0, o = 0; i < samples.Length; i += 4, o += 3)
            {
                working[o] = DecodeColor(ToDouble(samples[i]), transfer);
                working[o + 1] = DecodeColor(ToDouble(samples[i + 1]), transfer);
                working[o + 2] = DecodeColor(ToDouble(samples[i + 2]), transfer);
            }
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
            {
                working[i] = DecodeColor(ToDouble(samples[i]), transfer);
            }
        }
    }

    /// <summary>
    /// The <see cref="StoreScalar"/> of floating-point samples. With <see cref="WorkingAlpha.Premultiplied"/>: <c>A</c>
    /// clamped to [0, 1] is the output alpha; an alpha stored as zero gives transparent black, otherwise each color is
    /// unpremultiplied by the unclamped <c>A</c>. With <see cref="WorkingAlpha.Opaque"/>, the stored alpha is 1. Colors
    /// are then encoded (linear light: clamped to [0, 1], the domain of the function, and <c>Encode(L)</c> in double
    /// precision), and every value is rounded to the nearest sample, without clamping the colors.
    /// </summary>
    private static void StoreFloatingPoint<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, WorkingAlpha alpha, byte transfer)
        where TSample : unmanaged
    {
        if (alpha == WorkingAlpha.Opaque)
        {
            var opaque = FromDouble<TSample>(1);
            for (int i = 0, o = 0; i < samples.Length; i += 4, o += 3)
            {
                samples[i] = FromDouble<TSample>(EncodeFloatingPointColor(values[o], transfer));
                samples[i + 1] = FromDouble<TSample>(EncodeFloatingPointColor(values[o + 1], transfer));
                samples[i + 2] = FromDouble<TSample>(EncodeFloatingPointColor(values[o + 2], transfer));
                samples[i + 3] = opaque;
            }
        }
        else if (alpha == WorkingAlpha.Premultiplied)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                // An alpha that is not a number fails both comparisons and stays what it is
                var a = values[i + 3];
                var storedAlpha = FromDouble<TSample>(a < 0 ? 0 : a > 1 ? 1 : a);
                if (ToDouble(storedAlpha) == 0)
                {
                    samples.Slice(i, 4).Clear();
                    continue;
                }

                samples[i] = FromDouble<TSample>(EncodeFloatingPointColor(values[i] / a, transfer));
                samples[i + 1] = FromDouble<TSample>(EncodeFloatingPointColor(values[i + 1] / a, transfer));
                samples[i + 2] = FromDouble<TSample>(EncodeFloatingPointColor(values[i + 2] / a, transfer));
                samples[i + 3] = storedAlpha;
            }
        }
        else if (alpha == WorkingAlpha.Preserved)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                samples[i] = FromDouble<TSample>(EncodeFloatingPointColor(values[i], transfer));
                samples[i + 1] = FromDouble<TSample>(EncodeFloatingPointColor(values[i + 1], transfer));
                samples[i + 2] = FromDouble<TSample>(EncodeFloatingPointColor(values[i + 2], transfer));
            }
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = FromDouble<TSample>(EncodeFloatingPointColor(values[i], transfer));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DecodeColor(double value, byte transfer)
        => transfer is 0 ? value : CicpTransfer.Decode(transfer, value);

    private static double EncodeFloatingPointColor(double value, byte transfer)
    {
        if (transfer is 0)
            return value;

        var clamped = value <= 0 ? 0 : value >= 1 ? 1 : value;
        return transfer is CicpTransfer.Srgb ? SrgbTransfer.Encode(clamped) : CicpTransfer.Encode(transfer, clamped);
    }

    /// <summary>Reads a floating-point sample (<see cref="float"/> or <see cref="Half"/>), exactly.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ToDouble<TSample>(TSample value)
        where TSample : unmanaged
        => typeof(TSample) == typeof(float) ? unsafe(Unsafe.As<TSample, float>(ref value)) : (double)unsafe(Unsafe.As<TSample, Half>(ref value));

    /// <summary>Rounds a value to the nearest floating-point sample (<see cref="float"/> or <see cref="Half"/>), ties to even.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TSample FromDouble<TSample>(double value)
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(float))
        {
            var f = (float)value;
            return unsafe(Unsafe.As<float, TSample>(ref f));
        }

        var h = (Half)value;
        return unsafe(Unsafe.As<Half, TSample>(ref h));
    }

    /// <summary>Converts the leading encoded samples 4 at a time (whole pixels when there is alpha).</summary>
    /// <returns>The number of samples converted, a multiple of 4.</returns>
    private static int LoadVector<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha)
        where TSample : unmanaged
    {
        Debug.Assert(working.Length >= GetWorkingLength(samples.Length, alpha));
        if (alpha == WorkingAlpha.Opaque)
            return LoadOpaqueVector(samples, working);

        ref var input = ref unsafe(Unsafe.As<TSample, byte>(ref MemoryMarshal.GetReference(samples)));
        ref var output = ref unsafe(MemoryMarshal.GetReference(working));
        var length = samples.Length;
        var i = 0;
        if (typeof(TSample) == typeof(byte))
        {
            for (; i + 16 <= length; i += 16)
            {
                var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)i)));
                var (first, second) = Vector128.Widen(low);
                var (third, fourth) = Vector128.Widen(high);
                StoreWorking(first, ref output, i, alpha);
                StoreWorking(second, ref output, i + 4, alpha);
                StoreWorking(third, ref output, i + 8, alpha);
                StoreWorking(fourth, ref output, i + 12, alpha);
            }
        }
        else
        {
            for (; i + 8 <= length; i += 8)
            {
                var (first, second) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(2 * i))).AsUInt16());
                StoreWorking(first, ref output, i, alpha);
                StoreWorking(second, ref output, i + 4, alpha);
            }
        }

        return i;
    }

    /// <summary>
    /// Converts the colors of the leading opaque pixels, 4 pixels at a time: each pixel is stored as 4 doubles 3 elements
    /// after the previous one, so that the next pixel overwrites its alpha; the last pixel is left to the scalar loop, which
    /// writes nothing after the colors.
    /// </summary>
    /// <returns>The number of samples converted, a multiple of 4.</returns>
    private static int LoadOpaqueVector<TSample>(ReadOnlySpan<TSample> samples, Span<double> working)
        where TSample : unmanaged
    {
        ref var input = ref unsafe(Unsafe.As<TSample, byte>(ref MemoryMarshal.GetReference(samples)));
        ref var output = ref unsafe(MemoryMarshal.GetReference(working));
        var length = samples.Length;
        var i = 0;
        if (typeof(TSample) == typeof(byte))
        {
            for (; i + 16 < length; i += 16)
            {
                var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)i)));
                var (first, second) = Vector128.Widen(low);
                var (third, fourth) = Vector128.Widen(high);
                var o = i / 4 * 3;
                StoreWorking(first, ref output, o, WorkingAlpha.None);
                StoreWorking(second, ref output, o + 3, WorkingAlpha.None);
                StoreWorking(third, ref output, o + 6, WorkingAlpha.None);
                StoreWorking(fourth, ref output, o + 9, WorkingAlpha.None);
            }
        }
        else
        {
            for (; i + 8 < length; i += 8)
            {
                var (first, second) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(2 * i))).AsUInt16());
                var o = i / 4 * 3;
                StoreWorking(first, ref output, o, WorkingAlpha.None);
                StoreWorking(second, ref output, o + 3, WorkingAlpha.None);
            }
        }

        return i;
    }

    /// <summary>Stores 4 samples (one pixel when there is alpha) as working samples: <c>(r a, g a, b a, a)</c> when premultiplied, the samples otherwise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreWorking(Vector128<uint> samples, ref double output, int index, WorkingAlpha alpha)
    {
        // Samples up to 65535 are exact as floats, then as doubles
        var values = Vector128.ConvertToSingle(samples.AsInt32());
        var low = Vector128.WidenLower(values);
        var high = Vector128.WidenUpper(values);
        if (alpha == WorkingAlpha.Premultiplied)
        {
            // (r, g) * (a, a) and (b, a) * (a, 1): exact products of integers (at most 65535 * 65535)
            var a = Vector128.Create(high.GetElement(1));
            low *= a;
            high *= Vector128.ConditionalSelect(Vector128.Create(0, -1L).AsDouble(), Vector128<double>.One, a);
        }

        unsafe { low.StoreUnsafe(ref output, (nuint)index); }
        unsafe { high.StoreUnsafe(ref output, (nuint)(index + 2)); }
    }

    /// <summary>The 256-bit version of <see cref="LoadVector"/> (AVX2): the same samples, widened in the same way, converted to one vector of 4 doubles.</summary>
    /// <returns>The number of samples converted, a multiple of 4.</returns>
    private static int LoadVector256<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha)
        where TSample : unmanaged
    {
        Debug.Assert(working.Length >= samples.Length);
        ref var input = ref unsafe(Unsafe.As<TSample, byte>(ref MemoryMarshal.GetReference(samples)));
        ref var output = ref unsafe(MemoryMarshal.GetReference(working));
        var length = samples.Length;
        var i = 0;
        if (typeof(TSample) == typeof(byte))
        {
            for (; i + 16 <= length; i += 16)
            {
                var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)i)));
                var (first, second) = Vector128.Widen(low);
                var (third, fourth) = Vector128.Widen(high);
                StoreWorking256(first, ref output, i, alpha);
                StoreWorking256(second, ref output, i + 4, alpha);
                StoreWorking256(third, ref output, i + 8, alpha);
                StoreWorking256(fourth, ref output, i + 12, alpha);
            }
        }
        else
        {
            for (; i + 8 <= length; i += 8)
            {
                var (first, second) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(2 * i))).AsUInt16());
                StoreWorking256(first, ref output, i, alpha);
                StoreWorking256(second, ref output, i + 4, alpha);
            }
        }

        return i;
    }

    /// <summary>The 256-bit version of <see cref="StoreWorking"/>: <c>(r a, g a, b a, a)</c> when premultiplied, the samples otherwise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreWorking256(Vector128<uint> samples, ref double output, int index, WorkingAlpha alpha)
    {
        // Samples up to 65535 are exact as doubles
        var values = Avx.ConvertToVector256Double(samples.AsInt32());
        if (alpha == WorkingAlpha.Premultiplied)
        {
            // (r, g, b, a) * (a, a, a, 1): exact products of integers (at most 65535 * 65535)
            var a = Avx2.Permute4x64(values, 0b11_11_11_11);
            values *= Vector256.ConditionalSelect(Vector256.Create(0, 0, 0, -1L).AsDouble(), Vector256<double>.One, a);
        }

        unsafe { values.StoreUnsafe(ref output, (nuint)index); }
    }

    /// <summary>
    /// The 256-bit version of <see cref="StoreVector"/> (AVX2): one pixel per vector, one division of its colors by its
    /// unrounded alpha, the same rounding.
    /// </summary>
    /// <returns>The number of samples stored, a multiple of 4.</returns>
    private static int StoreVector256<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, bool premultiplied, int max)
        where TSample : unmanaged
    {
        Debug.Assert(samples.Length == values.Length);
        ref var input = ref unsafe(MemoryMarshal.GetReference(values));
        ref var output = ref unsafe(Unsafe.As<TSample, byte>(ref MemoryMarshal.GetReference(samples)));
        var maximum = Vector256.Create((double)max);
        var i = 0;
        for (; i + 4 <= samples.Length; i += 4)
        {
            var value = unsafe(Vector256.LoadUnsafe(ref input, (nuint)i));
            if (premultiplied)
            {
                // Colors are divided by the unrounded alpha, alpha itself is rounded (a division by a zero alpha gives a
                // meaningless value, replaced below)
                var a = Avx2.Permute4x64(value, 0b11_11_11_11);
                value = Vector256.ConditionalSelect(Vector256.Create(0, 0, 0, -1L).AsDouble(), value, value / a);
            }

            // The rounded samples are integers between 0 and max, exact as int32
            var rounded = Avx.ConvertToVector128Int32WithTruncation(Vector256.Min(Vector256.Max(Vector256.Floor(value + Vector256.Create(RoundingOffset)), Vector256<double>.Zero), maximum));
            if (premultiplied)
            {
                // A zero rounded alpha gives transparent black
                rounded &= ~Vector128.Equals(Vector128.Shuffle(rounded, Vector128.Create(3)), Vector128<int>.Zero);
            }

            var narrowed = Vector128.Narrow(rounded.AsUInt32(), Vector128<uint>.Zero);
            if (typeof(TSample) == typeof(byte))
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i), Vector128.Narrow(narrowed, Vector128<ushort>.Zero).AsUInt32().ToScalar()); }
            }
            else
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, 2 * i), narrowed.AsUInt64().ToScalar()); }
            }
        }

        return i;
    }

    /// <summary>Rounds the leading encoded working samples 4 at a time (whole pixels when <paramref name="premultiplied"/>).</summary>
    /// <returns>The number of samples stored, a multiple of 4.</returns>
    private static int StoreVector<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, bool premultiplied, int max)
        where TSample : unmanaged
    {
        Debug.Assert(samples.Length == values.Length);
        ref var input = ref unsafe(MemoryMarshal.GetReference(values));
        ref var output = ref unsafe(Unsafe.As<TSample, byte>(ref MemoryMarshal.GetReference(samples)));
        var maximum = Vector128.Create((double)max);
        var i = 0;
        for (; i + 4 <= samples.Length; i += 4)
        {
            var low = unsafe(Vector128.LoadUnsafe(ref input, (nuint)i));
            var high = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(i + 2)));
            if (premultiplied)
            {
                // Colors are divided by the unrounded alpha, alpha itself is rounded (a division by a zero alpha gives a
                // meaningless value, replaced below)
                var a = Vector128.Create(high.GetElement(1));
                low /= a;
                high = Vector128.ConditionalSelect(Vector128.Create(0, -1L).AsDouble(), high, high / a);
            }

            // The rounded samples are integers between 0 and max, exact as floats
            var rounded = Vector128.ConvertToInt32(Vector128.Narrow(RoundVector(low, maximum), RoundVector(high, maximum)));
            if (premultiplied)
            {
                // A zero rounded alpha gives transparent black
                rounded &= ~Vector128.Equals(Vector128.Shuffle(rounded, Vector128.Create(3)), Vector128<int>.Zero);
            }

            var narrowed = Vector128.Narrow(rounded.AsUInt32(), Vector128<uint>.Zero);
            if (typeof(TSample) == typeof(byte))
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i), Vector128.Narrow(narrowed, Vector128<ushort>.Zero).AsUInt32().ToScalar()); }
            }
            else
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, 2 * i), narrowed.AsUInt64().ToScalar()); }
            }
        }

        return i;
    }

    /// <summary>
    /// Rounds the colors of the leading opaque pixels, one pixel at a time: the 3 colors and the first value of the next
    /// pixel, whose lane becomes the maximum alpha. The last pixel is left to the scalar loop, which reads nothing after
    /// its colors.
    /// </summary>
    /// <returns>The number of samples stored, a multiple of 4.</returns>
    private static int StoreOpaqueVector<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, int max)
        where TSample : unmanaged
    {
        Debug.Assert(values.Length == GetWorkingLength(samples.Length, WorkingAlpha.Opaque));
        ref var input = ref unsafe(MemoryMarshal.GetReference(values));
        ref var output = ref unsafe(Unsafe.As<TSample, byte>(ref MemoryMarshal.GetReference(samples)));
        var maximum = Vector128.Create((double)max);
        var alpha = Vector128.Create(max);
        var alphaLane = Vector128.Create(0, 0, 0, -1);
        var i = 0;
        for (var o = 0; o + 4 <= values.Length; i += 4, o += 3)
        {
            var low = unsafe(Vector128.LoadUnsafe(ref input, (nuint)o));
            var high = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(o + 2)));
            var rounded = Vector128.ConditionalSelect(alphaLane, alpha, Vector128.ConvertToInt32(Vector128.Narrow(RoundVector(low, maximum), RoundVector(high, maximum))));
            var narrowed = Vector128.Narrow(rounded.AsUInt32(), Vector128<uint>.Zero);
            if (typeof(TSample) == typeof(byte))
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i), Vector128.Narrow(narrowed, Vector128<ushort>.Zero).AsUInt32().ToScalar()); }
            }
            else
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, 2 * i), narrowed.AsUInt64().ToScalar()); }
            }
        }

        return i;
    }

    /// <summary>The rounding of <see cref="Round"/> on 2 lanes: <c>floor(v + 0.5 + 2^-20)</c> clamped to [0, max].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> RoundVector(Vector128<double> value, Vector128<double> maximum)
        => Vector128.Min(Vector128.Max(Vector128.Floor(value + Vector128.Create(RoundingOffset)), Vector128<double>.Zero), maximum);

    /// <summary>Gets a value indicating whether a stored sample is zero (a floating-point sample of either sign).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsZero<TSample>(TSample value)
        where TSample : unmanaged
        => IsFloatingPoint<TSample>() ? ToDouble(value) == 0 : ToInt(value) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt<TSample>(TSample value)
        where TSample : unmanaged
        => typeof(TSample) == typeof(byte) ? unsafe(Unsafe.As<TSample, byte>(ref value)) : unsafe(Unsafe.As<TSample, ushort>(ref value));

    private static int EncodeColor(double value, int max, byte transfer, QuantizationThresholds? thresholds)
    {
        if (transfer is 0)
            return Round(value, max);

        if (thresholds is not null)
            return thresholds.Quantize(value);

        var clamped = value <= 0 ? 0 : value >= 1 ? 1 : value;
        return Round(max * (transfer is CicpTransfer.Srgb ? SrgbTransfer.Encode(clamped) : CicpTransfer.Encode(transfer, clamped)), max);
    }

    /// <summary>
    /// Rounds to nearest with ties upward and clamps to [0, max]: <c>floor(v + 0.5 + 2^-20)</c>. The bias makes exact
    /// rational ties (common when upsampling by rational factors) round upward even when the double-precision sum lands
    /// one unit in the last place below them; it is far larger than the accumulation error and far smaller than any
    /// meaningful sample difference.
    /// </summary>
    private static int Round(double value, int max)
    {
        var rounded = Math.Floor(value + RoundingOffset);
        return rounded <= 0 ? 0 : rounded >= max ? max : (int)rounded;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TSample FromInt<TSample>(int value)
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(byte))
        {
            var b = (byte)value;
            return unsafe(Unsafe.As<byte, TSample>(ref b));
        }

        var s = (ushort)value;
        return unsafe(Unsafe.As<ushort, TSample>(ref s));
    }
}
