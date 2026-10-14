using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The double-precision working samples of the filtering operations: conversion of stored
/// samples to working samples and back, shared by <see cref="Resampler"/> and <see cref="Convolver"/> so that both follow
/// the same premultiplication, transfer function, rounding and clamping rules.
/// </summary>
internal static class WorkingSamples
{
    /// <summary>The rounding offset: one half plus the tie bias <c>2^-20</c> (about 9.5e-7 sample units), exactly representable.</summary>
    internal const double RoundingOffset = 0.5 + (1.0 / (1 << 20));

    /// <summary>
    /// Converts stored samples to working samples: the encoded value <c>v</c> (encoded) or <c>Decode(v / max)</c> (linear
    /// light, with the function of the transfer characteristics <paramref name="transfer"/> of <see cref="CicpTransfer"/>;
    /// 0 keeps the encoded values). With <see cref="WorkingAlpha.Premultiplied"/>, colors are multiplied by the alpha sample <c>a</c> (0 to max);
    /// with <see cref="WorkingAlpha.Preserved"/> they are kept straight. In both cases the alpha working sample is <c>a</c>.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, encoded samples are converted 4 at a time: every sample and product is an integer below
    /// 2^32, so the conversions and the products are exact and give the doubles of <see cref="LoadScalar"/>.
    /// </remarks>
    public static void Load<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha, byte transfer)
        where TSample : unmanaged
    {
        var done = transfer is 0 && Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar ? LoadVector(samples, working, alpha) : 0;
        LoadScalar(samples[done..], working[done..], alpha, transfer);
    }

    /// <summary>The scalar reference of <see cref="Load"/>.</summary>
    internal static void LoadScalar<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha, byte transfer)
        where TSample : unmanaged
    {
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
    /// <c>A</c>. With <see cref="WorkingAlpha.Preserved"/>, the stored alpha sample is left untouched. Colors are then
    /// encoded (linear light: clamped to [0, 1] and <c>max * Encode(L)</c>), rounded with <c>floor(v + 0.5 + 2^-20)</c>
    /// and clamped to [0, max].
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, encoded samples without alpha or with premultiplied alpha are rounded 4 at a time with
    /// the same IEEE operations (division, addition, floor) as <see cref="StoreScalar"/>, so the results are identical.
    /// </remarks>
    public static void Store<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, WorkingAlpha alpha, byte transfer, int max)
        where TSample : unmanaged
    {
        var done = transfer is 0 && alpha != WorkingAlpha.Preserved && Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar ? StoreVector(values, samples, alpha == WorkingAlpha.Premultiplied, max) : 0;
        StoreScalar(values[done..], samples[done..], alpha, transfer, max);
    }

    /// <summary>The scalar reference of <see cref="Store"/>.</summary>
    internal static void StoreScalar<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, WorkingAlpha alpha, byte transfer, int max)
        where TSample : unmanaged
    {
        Debug.Assert(samples.Length == values.Length);
        if (alpha == WorkingAlpha.Premultiplied)
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

                samples[i] = FromInt<TSample>(EncodeColor(values[i] / a, max, transfer));
                samples[i + 1] = FromInt<TSample>(EncodeColor(values[i + 1] / a, max, transfer));
                samples[i + 2] = FromInt<TSample>(EncodeColor(values[i + 2] / a, max, transfer));
                samples[i + 3] = FromInt<TSample>(roundedAlpha);
            }
        }
        else if (alpha == WorkingAlpha.Preserved)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                samples[i] = FromInt<TSample>(EncodeColor(values[i], max, transfer));
                samples[i + 1] = FromInt<TSample>(EncodeColor(values[i + 1], max, transfer));
                samples[i + 2] = FromInt<TSample>(EncodeColor(values[i + 2], max, transfer));
            }
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = FromInt<TSample>(EncodeColor(values[i], max, transfer));
            }
        }
    }

    /// <summary>Converts the leading encoded samples 4 at a time (whole pixels when there is alpha).</summary>
    /// <returns>The number of samples converted, a multiple of 4.</returns>
    private static int LoadVector<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha)
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

    /// <summary>The rounding of <see cref="Round"/> on 2 lanes: <c>floor(v + 0.5 + 2^-20)</c> clamped to [0, max].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> RoundVector(Vector128<double> value, Vector128<double> maximum)
        => Vector128.Min(Vector128.Max(Vector128.Floor(value + Vector128.Create(RoundingOffset)), Vector128<double>.Zero), maximum);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt<TSample>(TSample value)
        where TSample : unmanaged
        => typeof(TSample) == typeof(byte) ? unsafe(Unsafe.As<TSample, byte>(ref value)) : unsafe(Unsafe.As<TSample, ushort>(ref value));

    private static int EncodeColor(double value, int max, byte transfer)
    {
        if (transfer is 0)
            return Round(value, max);

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
