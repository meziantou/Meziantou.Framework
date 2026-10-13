using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The multiple component transformations of T.800, Annex G, and the DC level shifting of G.1, which turn the output of
/// the inverse wavelet transformation into unsigned samples, and the samples of an image into the input of the forward
/// wavelet transformation.
/// </summary>
/// <remarks>
/// <para>
/// The numerical contract of the irreversible path: single precision, the constants of equations G-3 and G-4 rounded
/// once, each expression evaluated in the written order, no product fused with a sum; a sample is clamped to the range of
/// its depth, then rounded to the nearest integer, ties to even.
/// </para>
/// <para>
/// Every operation changes one sample from the samples at the same position: the vector paths compute what the scalar
/// ones compute, bit for bit.
/// </para>
/// </remarks>
internal static class Jpeg2000ColorTransform
{
    /// <summary>The inverse reversible component transformation (equation G-2), in place.</summary>
    /// <param name="y0">The first component; receives the red samples.</param>
    /// <param name="y1">The second component; receives the green samples.</param>
    /// <param name="y2">The third component; receives the blue samples.</param>
    public static void InverseReversible(Span<int> y0, Span<int> y1, Span<int> y2)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= y0.Length - Vector<int>.Count; i += Vector<int>.Count)
            {
                var first = new Vector<int>(y0[i..]);
                var second = new Vector<int>(y1[i..]);
                var third = new Vector<int>(y2[i..]);
                var green = first - ((third + second) >> 2);
                (third + green).CopyTo(y0[i..]);
                green.CopyTo(y1[i..]);
                (second + green).CopyTo(y2[i..]);
            }
        }

        InverseReversibleScalar(y0[i..], y1[i..], y2[i..]);
    }

    /// <summary>The scalar reference of <see cref="InverseReversible"/>.</summary>
    internal static void InverseReversibleScalar(Span<int> y0, Span<int> y1, Span<int> y2)
    {
        for (var i = 0; i < y0.Length; i++)
        {
            var green = y0[i] - ((y2[i] + y1[i]) >> 2);
            var red = y2[i] + green;
            var blue = y1[i] + green;
            y0[i] = red;
            y1[i] = green;
            y2[i] = blue;
        }
    }

    /// <summary>The inverse irreversible component transformation (equation G-4), in place.</summary>
    /// <param name="y0">The first component; receives the red samples.</param>
    /// <param name="y1">The second component; receives the green samples.</param>
    /// <param name="y2">The third component; receives the blue samples.</param>
    public static void InverseIrreversible(Span<float> y0, Span<float> y1, Span<float> y2)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var redFromRedDifference = new Vector<float>(1.402f);
            var greenFromBlueDifference = new Vector<float>(0.34413f);
            var greenFromRedDifference = new Vector<float>(0.71414f);
            var blueFromBlueDifference = new Vector<float>(1.772f);
            for (; i <= y0.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var luminance = new Vector<float>(y0[i..]);
                var blueDifference = new Vector<float>(y1[i..]);
                var redDifference = new Vector<float>(y2[i..]);
                var red = luminance + (redFromRedDifference * redDifference);
                var green = (luminance - (greenFromBlueDifference * blueDifference)) - (greenFromRedDifference * redDifference);
                var blue = luminance + (blueFromBlueDifference * blueDifference);
                red.CopyTo(y0[i..]);
                green.CopyTo(y1[i..]);
                blue.CopyTo(y2[i..]);
            }
        }

        InverseIrreversibleScalar(y0[i..], y1[i..], y2[i..]);
    }

    /// <summary>The scalar reference of <see cref="InverseIrreversible"/>.</summary>
    internal static void InverseIrreversibleScalar(Span<float> y0, Span<float> y1, Span<float> y2)
    {
        for (var i = 0; i < y0.Length; i++)
        {
            var luminance = y0[i];
            var blueDifference = y1[i];
            var redDifference = y2[i];
            var red = luminance + (1.402f * redDifference);
            var green = (luminance - (0.34413f * blueDifference)) - (0.71414f * redDifference);
            var blue = luminance + (1.772f * blueDifference);
            y0[i] = red;
            y1[i] = green;
            y2[i] = blue;
        }
    }

    /// <summary>One component of the forward reversible component transformation (equation G-1).</summary>
    /// <param name="red">The red samples, level shifted.</param>
    /// <param name="green">The green samples, level shifted.</param>
    /// <param name="blue">The blue samples, level shifted.</param>
    /// <param name="component">The component to compute: 0, 1 or 2.</param>
    /// <param name="destination">Receives the component.</param>
    public static void ForwardReversible(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, int component, Span<int> destination)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= destination.Length - Vector<int>.Count; i += Vector<int>.Count)
            {
                var reds = new Vector<int>(red[i..]);
                var greens = new Vector<int>(green[i..]);
                var blues = new Vector<int>(blue[i..]);
                var value = component switch
                {
                    0 => (reds + (greens + greens) + blues) >> 2,
                    1 => blues - greens,
                    _ => reds - greens,
                };
                value.CopyTo(destination[i..]);
            }
        }

        ForwardReversibleScalar(red[i..], green[i..], blue[i..], component, destination[i..]);
    }

    /// <summary>The scalar reference of <see cref="ForwardReversible"/>.</summary>
    internal static void ForwardReversibleScalar(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, int component, Span<int> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = component switch
            {
                0 => (red[i] + (2 * green[i]) + blue[i]) >> 2,
                1 => blue[i] - green[i],
                _ => red[i] - green[i],
            };
        }
    }

    /// <summary>One component of the forward irreversible component transformation (equation G-3).</summary>
    /// <param name="red">The red samples, level shifted.</param>
    /// <param name="green">The green samples, level shifted.</param>
    /// <param name="blue">The blue samples, level shifted.</param>
    /// <param name="component">The component to compute: 0, 1 or 2.</param>
    /// <param name="destination">Receives the component.</param>
    public static void ForwardIrreversible(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, int component, Span<float> destination)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var (redFactor, greenFactor, blueFactor) = GetForwardFactors(component);
            var redFactors = new Vector<float>(redFactor);
            var greenFactors = new Vector<float>(greenFactor);
            var blueFactors = new Vector<float>(blueFactor);
            for (; i <= destination.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var reds = redFactors * Vector.ConvertToSingle(new Vector<int>(red[i..]));
                var greens = greenFactors * Vector.ConvertToSingle(new Vector<int>(green[i..]));
                var blues = blueFactors * Vector.ConvertToSingle(new Vector<int>(blue[i..]));
                var value = component switch
                {
                    0 => (reds + greens) + blues,
                    1 => (reds - greens) + blues,
                    _ => (reds - greens) - blues,
                };
                value.CopyTo(destination[i..]);
            }
        }

        ForwardIrreversibleScalar(red[i..], green[i..], blue[i..], component, destination[i..]);
    }

    /// <summary>The scalar reference of <see cref="ForwardIrreversible"/>.</summary>
    internal static void ForwardIrreversibleScalar(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, int component, Span<float> destination)
    {
        var (redFactor, greenFactor, blueFactor) = GetForwardFactors(component);
        for (var i = 0; i < destination.Length; i++)
        {
            var reds = redFactor * red[i];
            var greens = greenFactor * green[i];
            var blues = blueFactor * blue[i];
            destination[i] = component switch
            {
                0 => (reds + greens) + blues,
                1 => (reds - greens) + blues,
                _ => (reds - greens) - blues,
            };
        }
    }

    /// <summary>Shifts integer samples by half of their range (equation G-1) and clamps them to the range of their depth, in place.</summary>
    /// <param name="samples">The samples.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    public static void ShiftLevel(Span<int> samples, int precision)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var offset = 1 << (precision - 1);
            var maximum = (1 << precision) - 1;
            var offsets = new Vector<int>(offset);
            var maximums = new Vector<int>(maximum);
            for (; i <= samples.Length - Vector<int>.Count; i += Vector<int>.Count)
            {
                Vector.Min(Vector.Max(new Vector<int>(samples[i..]) + offsets, Vector<int>.Zero), maximums).CopyTo(samples[i..]);
            }
        }

        ShiftLevelScalar(samples[i..], precision);
    }

    /// <summary>The scalar reference of <see cref="ShiftLevel(Span{int}, int)"/>.</summary>
    internal static void ShiftLevelScalar(Span<int> samples, int precision)
    {
        var offset = 1 << (precision - 1);
        var maximum = (1 << precision) - 1;
        foreach (ref var sample in samples)
        {
            sample = Math.Clamp(sample + offset, 0, maximum);
        }
    }

    /// <summary>Rounds floating-point samples, shifts them by half of their range and clamps them to the range of their depth.</summary>
    /// <param name="source">The samples.</param>
    /// <param name="destination">Receives the samples; it may be the memory of <paramref name="source"/>.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    public static void ShiftLevel(ReadOnlySpan<float> source, Span<int> destination, int precision)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            // The clamp is written as the two comparisons of Math.Clamp, which leave a value that is not a number as it is
            var offset = 1 << (precision - 1);
            float minimum = -offset;
            float maximum = offset - 1;
            var offsets = new Vector<int>(offset);
            var minimums = new Vector<float>(minimum);
            var maximums = new Vector<float>(maximum);
            for (; i <= source.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var value = new Vector<float>(source[i..]);
                value = Vector.ConditionalSelect(Vector.LessThan(value, minimums), minimums, Vector.ConditionalSelect(Vector.GreaterThan(value, maximums), maximums, value));
                (Vector.ConvertToInt32(Vector.Round(value)) + offsets).CopyTo(destination[i..]);
            }
        }

        ShiftLevelScalar(source[i..], destination[i..], precision);
    }

    /// <summary>The scalar reference of <see cref="ShiftLevel(ReadOnlySpan{float}, Span{int}, int)"/>.</summary>
    internal static void ShiftLevelScalar(ReadOnlySpan<float> source, Span<int> destination, int precision)
    {
        var offset = 1 << (precision - 1);
        float minimum = -offset;
        float maximum = offset - 1;
        for (var i = 0; i < source.Length; i++)
        {
            // A value that is not a number cannot be produced by finite coefficients; it would become the middle of the range
            var value = MathF.Round(Math.Clamp(source[i], minimum, maximum), MidpointRounding.ToEven);
            destination[i] = (int)value + offset;
        }
    }

    /// <summary>Gets the factors of one row of the matrix of equation G-3, as the expressions use them: a term that is subtracted has a positive factor.</summary>
    private static (float Red, float Green, float Blue) GetForwardFactors(int component) => component switch
    {
        0 => (0.299f, 0.587f, 0.114f),
        1 => (-0.16875f, 0.33126f, 0.5f),
        _ => (0.5f, 0.41869f, 0.08131f),
    };
}
