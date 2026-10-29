using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The filter the encoder applies to the samples of a frame whose decoder applies the Gabor-like filter
/// (<see cref="JpegXlRestorationFilters.ApplyGaborish"/>, a 3x3 smoothing): a sharpening of 5x5 samples that the smoothing
/// undoes, so that the decoded frame is the frame that was given, and the smoothing only removes what the quantization
/// added at the edges of the blocks.
/// </summary>
/// <remarks>
/// The weights are not written out: they are computed from the weights of the decoder. With <c>S</c> the smoothing and
/// <c>D = I - S</c> what it removes, the inverse of <c>S</c> is <c>I + D + D^2 + ...</c>; the filter is its first three
/// terms, <c>3 I - 3 S + S^2</c>, which reads 5x5 samples. The decoder then gives back <c>I - D^3</c>: a constant and a
/// slow variation exactly, and all but a sixth of the highest frequency, which the quantization removes first. Every
/// operation on the samples is a float32 one in a fixed order.
/// </remarks>
internal static class JpegXlInverseGaborish
{
    /// <summary>The number of samples the filter reads on each side of a sample.</summary>
    public const int Radius = 2;

    /// <summary>The number of distinct weights of the kernel: the center, then the samples at (1, 0), (1, 1), (2, 0), (2, 1) and (2, 2), and their symmetric ones.</summary>
    public const int WeightCount = 6;

    /// <summary>Computes the weights of the kernel that undoes the filter of the decoder for one channel.</summary>
    /// <param name="side">The weight of the four adjacent samples in the filter of the decoder, relative to its center.</param>
    /// <param name="corner">The weight of the four diagonal samples.</param>
    /// <param name="weights">Receives the <see cref="WeightCount"/> weights.</param>
    public static void GetWeights(float side, float corner, Span<float> weights)
    {
        // The kernel of the decoder, normalized as it normalizes it
        var scale = 1.0 / (1.0 + (4.0 * ((double)side + corner)));
        Span<double> smoothing = stackalloc double[9];
        for (var y = -1; y <= 1; y++)
        {
            for (var x = -1; x <= 1; x++)
            {
                smoothing[((y + 1) * 3) + x + 1] = (x == 0 && y == 0 ? 1.0 : x == 0 || y == 0 ? side : corner) * scale;
            }
        }

        // The smoothing applied twice: 5x5 values
        const int Size = (2 * Radius) + 1;
        Span<double> twice = stackalloc double[Size * Size];
        twice.Clear();
        for (var y = -1; y <= 1; y++)
        {
            for (var x = -1; x <= 1; x++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        twice[((y + dy + Radius) * Size) + x + dx + Radius] += smoothing[((y + 1) * 3) + x + 1] * smoothing[((dy + 1) * 3) + dx + 1];
                    }
                }
            }
        }

        // 3 I - 3 S + S S, one value for each distinct weight
        ReadOnlySpan<(int X, int Y)> positions = [(0, 0), (1, 0), (1, 1), (2, 0), (2, 1), (2, 2)];
        for (var i = 0; i < WeightCount; i++)
        {
            var (x, y) = positions[i];
            var value = twice[((y + Radius) * Size) + x + Radius];
            if (x <= 1 && y <= 1)
            {
                value -= 3 * smoothing[((y + 1) * 3) + x + 1];
            }

            if (i == 0)
            {
                value += 3;
            }

            weights[i] = (float)value;
        }
    }

    /// <summary>Filters a rectangle of samples.</summary>
    /// <param name="source">The samples, with <see cref="Radius"/> more on each side of the rectangle: the first sample of the rectangle is at that distance of the first row and of the first column.</param>
    /// <param name="sourceStride">The distance between two rows of <paramref name="source"/>.</param>
    /// <param name="destination">Receives the samples of the rectangle.</param>
    /// <param name="destinationStride">The distance between two rows of <paramref name="destination"/>.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <param name="weights">The weights, as <see cref="GetWeights"/> computes them.</param>
    public static void Apply(ReadOnlySpan<float> source, int sourceStride, Span<float> destination, int destinationStride, int width, int height, ReadOnlySpan<float> weights)
    {
        var center = weights[0];
        var side = weights[1];
        var corner = weights[2];
        var far = weights[3];
        var knight = weights[4];
        var farCorner = weights[5];
        for (var y = 0; y < height; y++)
        {
            var row0 = source.Slice(y * sourceStride, width + (2 * Radius));
            var row1 = source.Slice((y + 1) * sourceStride, width + (2 * Radius));
            var row2 = source.Slice((y + 2) * sourceStride, width + (2 * Radius));
            var row3 = source.Slice((y + 3) * sourceStride, width + (2 * Radius));
            var row4 = source.Slice((y + 4) * sourceStride, width + (2 * Radius));
            var output = destination.Slice(y * destinationStride, width);
            var x = 0;
            if (Vector.IsHardwareAccelerated && width >= Vector<float>.Count)
            {
                // The same sums in the same order as below, a vector of samples at a time
                ref var r0 = ref unsafe(MemoryMarshal.GetReference(row0));
                ref var r1 = ref unsafe(MemoryMarshal.GetReference(row1));
                ref var r2 = ref unsafe(MemoryMarshal.GetReference(row2));
                ref var r3 = ref unsafe(MemoryMarshal.GetReference(row3));
                ref var r4 = ref unsafe(MemoryMarshal.GetReference(row4));
                ref var target = ref unsafe(MemoryMarshal.GetReference(output));
                var centerVector = new Vector<float>(center);
                var sideVector = new Vector<float>(side);
                var cornerVector = new Vector<float>(corner);
                var farVector = new Vector<float>(far);
                var knightVector = new Vector<float>(knight);
                var farCornerVector = new Vector<float>(farCorner);
                for (; x <= width - Vector<float>.Count; x += Vector<float>.Count)
                {
                    var offset = (nuint)x;
                    var sides = (Load(ref r2, offset + 1) + Load(ref r2, offset + 3)) + (Load(ref r1, offset + 2) + Load(ref r3, offset + 2));
                    var corners = (Load(ref r1, offset + 1) + Load(ref r1, offset + 3)) + (Load(ref r3, offset + 1) + Load(ref r3, offset + 3));
                    var fars = (Load(ref r2, offset) + Load(ref r2, offset + 4)) + (Load(ref r0, offset + 2) + Load(ref r4, offset + 2));
                    var knights = ((Load(ref r0, offset + 1) + Load(ref r0, offset + 3)) + (Load(ref r4, offset + 1) + Load(ref r4, offset + 3))) + ((Load(ref r1, offset) + Load(ref r1, offset + 4)) + (Load(ref r3, offset) + Load(ref r3, offset + 4)));
                    var farCorners = (Load(ref r0, offset) + Load(ref r0, offset + 4)) + (Load(ref r4, offset) + Load(ref r4, offset + 4));
                    var value = (Load(ref r2, offset + 2) * centerVector) + (sides * sideVector) + (corners * cornerVector) + (fars * farVector) + (knights * knightVector) + (farCorners * farCornerVector);
                    unsafe
                    {
                        value.StoreUnsafe(ref target, offset);
                    }
                }
            }

            for (; x < width; x++)
            {
                var sides = (row2[x + 1] + row2[x + 3]) + (row1[x + 2] + row3[x + 2]);
                var corners = (row1[x + 1] + row1[x + 3]) + (row3[x + 1] + row3[x + 3]);
                var fars = (row2[x] + row2[x + 4]) + (row0[x + 2] + row4[x + 2]);
                var knights = ((row0[x + 1] + row0[x + 3]) + (row4[x + 1] + row4[x + 3])) + ((row1[x] + row1[x + 4]) + (row3[x] + row3[x + 4]));
                var farCorners = (row0[x] + row0[x + 4]) + (row4[x] + row4[x + 4]);
                output[x] = (row2[x + 2] * center) + (sides * side) + (corners * corner) + (fars * far) + (knights * knight) + (farCorners * farCorner);
            }
        }

        static Vector<float> Load(ref float source, nuint offset) => unsafe(Vector.LoadUnsafe(ref source, offset));
    }
}
