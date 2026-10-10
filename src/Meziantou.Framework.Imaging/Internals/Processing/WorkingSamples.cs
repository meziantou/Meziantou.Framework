using System.Diagnostics;
using System.Runtime.CompilerServices;

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
    /// light). With <see cref="WorkingAlpha.Premultiplied"/>, colors are multiplied by the alpha sample <c>a</c> (0 to max);
    /// with <see cref="WorkingAlpha.Preserved"/> they are kept straight. In both cases the alpha working sample is <c>a</c>.
    /// </summary>
    public static void Load<TSample>(ReadOnlySpan<TSample> samples, Span<double> working, WorkingAlpha alpha, bool linear)
        where TSample : unmanaged
    {
        var decode = !linear ? default : typeof(TSample) == typeof(byte) ? SrgbTransfer.Decode8 : SrgbTransfer.Decode16;
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
    public static void Store<TSample>(ReadOnlySpan<double> values, Span<TSample> samples, WorkingAlpha alpha, bool linear, int max)
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

                samples[i] = FromInt<TSample>(EncodeColor(values[i] / a, max, linear));
                samples[i + 1] = FromInt<TSample>(EncodeColor(values[i + 1] / a, max, linear));
                samples[i + 2] = FromInt<TSample>(EncodeColor(values[i + 2] / a, max, linear));
                samples[i + 3] = FromInt<TSample>(roundedAlpha);
            }
        }
        else if (alpha == WorkingAlpha.Preserved)
        {
            for (var i = 0; i < samples.Length; i += 4)
            {
                samples[i] = FromInt<TSample>(EncodeColor(values[i], max, linear));
                samples[i + 1] = FromInt<TSample>(EncodeColor(values[i + 1], max, linear));
                samples[i + 2] = FromInt<TSample>(EncodeColor(values[i + 2], max, linear));
            }
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = FromInt<TSample>(EncodeColor(values[i], max, linear));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt<TSample>(TSample value)
        where TSample : unmanaged
        => typeof(TSample) == typeof(byte) ? unsafe(Unsafe.As<TSample, byte>(ref value)) : unsafe(Unsafe.As<TSample, ushort>(ref value));

    private static int EncodeColor(double value, int max, bool linear)
    {
        if (!linear)
            return Round(value, max);

        var clamped = value <= 0 ? 0 : value >= 1 ? 1 : value;
        return Round(max * SrgbTransfer.Encode(clamped), max);
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
