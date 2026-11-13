using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The blend modes of "Compositing and Blending Level 1" of the W3C, on colors from 0 to 1 that are not multiplied by
/// their opacity: what the paints of a color glyph and the brushes of the drawing API mix colors with.
/// </summary>
/// <remarks>Only additions, multiplications, divisions and square roots are used: the colors are the same on every platform.</remarks>
internal static class BlendFunctions
{
    /// <summary>Blends a source color with a backdrop color.</summary>
    /// <param name="mode">The blend mode.</param>
    /// <param name="source">The red, green and blue of the source.</param>
    /// <param name="backdrop">The red, green and blue of the backdrop.</param>
    /// <param name="result">The blended color.</param>
    public static void Blend(BlendMode mode, ReadOnlySpan<double> source, ReadOnlySpan<double> backdrop, Span<double> result)
    {
        if (mode >= BlendMode.Hue)
        {
            BlendNonSeparable(mode, source, backdrop, result);
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            result[i] = Blend(mode, source[i], backdrop[i]);
        }
    }

    /// <summary>Blends a component of a source color with the one of a backdrop color: the separable blend modes.</summary>
    public static double Blend(BlendMode mode, double s, double d)
    {
        switch (mode)
        {
            case BlendMode.Normal:
                return s;

            case BlendMode.Screen:
                return s + d - (s * d);

            case BlendMode.Overlay:
                return HardLight(d, s);

            case BlendMode.Darken:
                return Math.Min(s, d);

            case BlendMode.Lighten:
                return Math.Max(s, d);

            case BlendMode.ColorDodge:
                return d <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1, d / (1 - s));

            case BlendMode.ColorBurn:
                return d >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1, (1 - d) / s);

            case BlendMode.HardLight:
                return HardLight(s, d);

            case BlendMode.SoftLight:
                {
                    if (s <= 0.5)
                        return d - ((1 - (2 * s)) * d * (1 - d));

                    var g = d <= 0.25 ? ((((16 * d) - 12) * d) + 4) * d : Math.Sqrt(d);
                    return d + (((2 * s) - 1) * (g - d));
                }

            case BlendMode.Difference:
                return Math.Abs(d - s);

            case BlendMode.Exclusion:
                return s + d - (2 * s * d);

            default:
                return s * d;
        }

        static double HardLight(double s, double d) => s <= 0.5 ? d * 2 * s : (2 * s) - 1 + d - (((2 * s) - 1) * d);
    }

    /// <summary>The blend modes that take a color as a whole: its hue, its saturation and its luminosity.</summary>
    private static void BlendNonSeparable(BlendMode mode, ReadOnlySpan<double> s, ReadOnlySpan<double> d, Span<double> result)
    {
        switch (mode)
        {
            case BlendMode.Hue:
                s.CopyTo(result);
                SetSaturation(result, Saturation(d));
                SetLuminosity(result, Luminosity(d));
                break;

            case BlendMode.Saturation:
                d.CopyTo(result);
                SetSaturation(result, Saturation(s));
                SetLuminosity(result, Luminosity(d));
                break;

            case BlendMode.Color:
                s.CopyTo(result);
                SetLuminosity(result, Luminosity(d));
                break;

            default:
                d.CopyTo(result);
                SetLuminosity(result, Luminosity(s));
                break;
        }

        static double Luminosity(ReadOnlySpan<double> color) => (0.3 * color[0]) + (0.59 * color[1]) + (0.11 * color[2]);

        static double Saturation(ReadOnlySpan<double> color) => Math.Max(color[0], Math.Max(color[1], color[2])) - Math.Min(color[0], Math.Min(color[1], color[2]));

        static void SetLuminosity(Span<double> color, double luminosity)
        {
            var shift = luminosity - Luminosity(color);
            for (var i = 0; i < 3; i++)
            {
                color[i] += shift;
            }

            // The color is brought back between 0 and 1, around its luminosity.
            var l = Luminosity(color);
            var min = Math.Min(color[0], Math.Min(color[1], color[2]));
            var max = Math.Max(color[0], Math.Max(color[1], color[2]));
            for (var i = 0; i < 3; i++)
            {
                if (min < 0)
                {
                    color[i] = l + ((color[i] - l) * l / (l - min));
                }

                if (max > 1)
                {
                    color[i] = l + ((color[i] - l) * (1 - l) / (max - l));
                }
            }
        }

        static void SetSaturation(Span<double> color, double saturation)
        {
            var min = Math.Min(color[0], Math.Min(color[1], color[2]));
            var max = Math.Max(color[0], Math.Max(color[1], color[2]));
            for (var i = 0; i < 3; i++)
            {
                color[i] = max > min ? (color[i] - min) * saturation / (max - min) : 0;
            }
        }
    }

    /// <summary>
    /// A separable blend mode on the lanes of vectors: <see cref="BlendFunctions.Blend(BlendMode, double, double)"/> of each lane, with the
    /// same operations in the same order, every branch computed and selected.
    /// </summary>
    /// <remarks>The components are from 0 to 1: the minimum and the maximum of two of them have a single definition.</remarks>
    internal interface ISeparableBlend
    {
        static abstract TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged;
    }

    internal readonly struct MultiplyBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
            => TLanes.Multiply(s, d);
    }

    internal readonly struct ScreenBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
            => TLanes.Subtract(TLanes.Add(s, d), TLanes.Multiply(s, d));
    }

    internal readonly struct OverlayBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
            => HardLightBlend.Blend<TLanes, TVector>(d, s);
    }

    internal readonly struct DarkenBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
            => TLanes.Min(s, d);
    }

    internal readonly struct LightenBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
            => TLanes.Max(s, d);
    }

    internal readonly struct ColorDodgeBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
        {
            var zero = TLanes.Create(0);
            var one = TLanes.Create(1);
            var result = TLanes.Min(one, TLanes.Divide(d, TLanes.Subtract(one, s)));
            result = TLanes.Select(TLanes.GreaterThanOrEqual(s, one), one, result);
            return TLanes.Select(TLanes.LessThanOrEqual(d, zero), zero, result);
        }
    }

    internal readonly struct ColorBurnBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
        {
            var zero = TLanes.Create(0);
            var one = TLanes.Create(1);
            var result = TLanes.Subtract(one, TLanes.Min(one, TLanes.Divide(TLanes.Subtract(one, d), s)));
            result = TLanes.Select(TLanes.LessThanOrEqual(s, zero), zero, result);
            return TLanes.Select(TLanes.GreaterThanOrEqual(d, one), one, result);
        }
    }

    internal readonly struct HardLightBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
        {
            var two = TLanes.Create(2);
            var doubled = TLanes.Multiply(two, s);
            var low = TLanes.Multiply(TLanes.Multiply(d, two), s);
            var shifted = TLanes.Subtract(doubled, TLanes.Create(1));
            var high = TLanes.Subtract(TLanes.Add(shifted, d), TLanes.Multiply(shifted, d));
            return TLanes.Select(TLanes.LessThanOrEqual(s, TLanes.Create(0.5)), low, high);
        }
    }

    internal readonly struct SoftLightBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
        {
            var one = TLanes.Create(1);
            var doubled = TLanes.Multiply(TLanes.Create(2), s);
            var low = TLanes.Subtract(d, TLanes.Multiply(TLanes.Multiply(TLanes.Subtract(one, doubled), d), TLanes.Subtract(one, d)));
            var polynomial = TLanes.Multiply(TLanes.Add(TLanes.Multiply(TLanes.Subtract(TLanes.Multiply(TLanes.Create(16), d), TLanes.Create(12)), d), TLanes.Create(4)), d);
            var g = TLanes.Select(TLanes.LessThanOrEqual(d, TLanes.Create(0.25)), polynomial, TLanes.Sqrt(d));
            var high = TLanes.Add(d, TLanes.Multiply(TLanes.Subtract(doubled, one), TLanes.Subtract(g, d)));
            return TLanes.Select(TLanes.LessThanOrEqual(s, TLanes.Create(0.5)), low, high);
        }
    }

    internal readonly struct DifferenceBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
            => TLanes.Abs(TLanes.Subtract(d, s));
    }

    internal readonly struct ExclusionBlend : ISeparableBlend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector Blend<TLanes, TVector>(TVector s, TVector d)
            where TLanes : IDoubleLanes<TVector>
            where TVector : unmanaged
            => TLanes.Subtract(TLanes.Add(s, d), TLanes.Multiply(TLanes.Multiply(TLanes.Create(2), s), d));
    }
}
