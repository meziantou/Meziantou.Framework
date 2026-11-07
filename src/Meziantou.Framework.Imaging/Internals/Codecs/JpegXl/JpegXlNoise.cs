using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The noise that a frame asks the decoder to synthesize (ISO/IEC 18181-1, sections C.4.7 and K.5): pseudo-random
/// values, the same for every decoder, high-pass filtered and added to the XYB samples with a strength that depends on
/// their intensity.
/// </summary>
/// <remarks>
/// The random values of each square of the size of a group come from a xorshift128+ generator of eight lanes, seeded with
/// the index of the frame among the frames that are shown, the number of frames that are not shown since, and the
/// position of the square: the synthesis does not depend on the order in which the squares are produced.
/// </remarks>
internal static class JpegXlNoise
{
    /// <summary>The number of points of the strength curve.</summary>
    public const int PointCount = 8;

    private const int LaneCount = 8;
    private const int FloatsPerBatch = LaneCount * 2;

    /// <summary>Reads the strength curve: eight values of 10 bits, in units of 1/1024.</summary>
    public static float[] ReadParameters(JpegXlBitReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var result = new float[PointCount];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = reader.ReadBits(10) / 1024f;
        }

        return result;
    }

    /// <summary>Adds the noise to the XYB samples of a frame, at its final resolution.</summary>
    /// <param name="frame">The samples of the frame: X, Y and B.</param>
    /// <param name="strengths">The strength curve.</param>
    /// <param name="groupDimension">The side of the squares that have their own random sequence.</param>
    /// <param name="visibleFrameIndex">The number of frames that are shown up to this one, this one included.</param>
    /// <param name="invisibleFrameIndex">The number of frames that are not shown since the last frame that is shown, this one included.</param>
    /// <param name="baseCorrelationX">The base factor of the prediction of X from Y of the frame.</param>
    /// <param name="baseCorrelationB">The base factor of the prediction of B from Y of the frame.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void Apply(JpegXlImageBuffer frame, ReadOnlySpan<float> strengths, int groupDimension, uint visibleFrameIndex, uint invisibleFrameIndex, float baseCorrelationX, float baseCorrelationB, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(scope);
        var hasNoise = false;
        foreach (var strength in strengths)
        {
            hasNoise |= MathF.Abs(strength) > 1e-3f;
        }

        if (!hasNoise)
            return;

        var width = frame.Width;
        var height = frame.Height;
        var length = width * height;
        using var noise = Av1PooledArray<float>.Rent(scope, 3L * length, clear: false);
        using var filtered = Av1PooledArray<float>.Rent(scope, length, clear: false);
        Span<ulong> state0 = stackalloc ulong[LaneCount];
        Span<ulong> state1 = stackalloc ulong[LaneCount];
        Span<float> batch = stackalloc float[FloatsPerBatch];
        for (var y0 = 0; y0 < height; y0 += groupDimension)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x0 = 0; x0 < width; x0 += groupDimension)
            {
                Seed(state0, state1, visibleFrameIndex, invisibleFrameIndex, (uint)x0, (uint)y0);
                var squareWidth = Math.Min(groupDimension, width - x0);
                var squareHeight = Math.Min(groupDimension, height - y0);
                for (var c = 0; c < 3; c++)
                {
                    for (var y = 0; y < squareHeight; y++)
                    {
                        // Every row starts a new batch of 16 values
                        var row = noise.Span.Slice((c * length) + ((y0 + y) * width) + x0, squareWidth);
                        for (var x = 0; x < squareWidth; x += FloatsPerBatch)
                        {
                            FillBatch(state0, state1, batch);
                            batch[..Math.Min(FloatsPerBatch, squareWidth - x)].CopyTo(row[x..]);
                        }
                    }
                }
            }
        }

        // A high-pass filter: 0.16 times the 24 neighbors minus 3.84 times the value, so that a constant gives zero
        for (var c = 0; c < 3; c++)
        {
            var plane = noise.Span.Slice(c * length, length);
            var output = filtered.Span[..length];
            HighPass(plane, output, width, height, cancellationToken);
            output.CopyTo(plane);
        }

        for (var y = 0; y < height; y++)
        {
            var rowX = frame.GetRow(0, y);
            var rowY = frame.GetRow(1, y);
            var rowB = frame.GetRow(2, y);
            var noiseR = noise.Span.Slice(y * width, width);
            var noiseG = noise.Span.Slice(length + (y * width), width);
            var noiseC = noise.Span.Slice((2 * length) + (y * width), width);
            for (var x = 0; x < width; x++)
            {
                // The strength follows the intensity of the red and of the green channel; most of the noise is common to both
                var strengthG = GetStrength(strengths, (rowY[x] - rowX[x]) * 0.5f);
                var strengthR = GetStrength(strengths, (rowY[x] + rowX[x]) * 0.5f);
                var common = noiseC[x] * 0.22f * 0.9921875f;
                var red = strengthR * ((0.0078125f * (noiseR[x] * 0.22f)) + common);
                var green = strengthG * ((0.0078125f * (noiseG[x] * 0.22f)) + common);
                var sum = red + green;
                rowX[x] += (baseCorrelationX * sum) + (red - green);
                rowY[x] += sum;
                rowB[x] += baseCorrelationB * sum;
            }
        }
    }

    /// <summary>
    /// The high-pass filter of the noise: 0.16 times the 24 neighbors minus 3.84 times the value, the neighbors outside the
    /// plane being the mirror image of those inside. Away from the left and right edges, four samples are filtered at a
    /// time, one per lane, with the additions of <see cref="HighPassScalar"/> in the same order.
    /// </summary>
    internal static void HighPass(ReadOnlySpan<float> plane, Span<float> output, int width, int height, CancellationToken cancellationToken)
    {
        if (!Vector128.IsHardwareAccelerated)
        {
            HighPassScalar(plane, output, width, height, cancellationToken);
            return;
        }

        Span<int> rows = stackalloc int[5];
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var dy = -2; dy <= 2; dy++)
            {
                rows[dy + 2] = Mirror(y + dy, height) * width;
            }

            // The samples from 2 on whose four lanes have their two neighbors on the right inside the row
            var x = Math.Min(2, width);
            HighPassColumns(plane, output, width, height, y, 0, x);
            var center = plane.Slice(y * width, width);
            var outputRow = output.Slice(y * width, width);
            for (; x + Vector128<float>.Count + 2 <= width; x += Vector128<float>.Count)
            {
                var others = AddRow(Vector128<float>.Zero, plane.Slice(rows[0] + x - 2, 8), includeCenter: true);
                others = AddRow(others, plane.Slice(rows[1] + x - 2, 8), includeCenter: true);
                others = AddRow(others, plane.Slice(rows[2] + x - 2, 8), includeCenter: false);
                others = AddRow(others, plane.Slice(rows[3] + x - 2, 8), includeCenter: true);
                others = AddRow(others, plane.Slice(rows[4] + x - 2, 8), includeCenter: true);

                var result = (others * Vector128.Create(0.16f)) + (Vector128.Create(center.Slice(x, Vector128<float>.Count)) * Vector128.Create(-3.84f));
                result.CopyTo(outputRow.Slice(x, Vector128<float>.Count));
            }

            HighPassColumns(plane, output, width, height, y, x, width);
        }
    }

    /// <summary>Adds the five values of a row around four samples (four without the center one), left to right.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> AddRow(Vector128<float> sum, ReadOnlySpan<float> row, bool includeCenter)
    {
        sum += Vector128.Create(row[..4]);
        sum += Vector128.Create(row.Slice(1, 4));
        if (includeCenter)
        {
            sum += Vector128.Create(row.Slice(2, 4));
        }

        sum += Vector128.Create(row.Slice(3, 4));
        return sum + Vector128.Create(row.Slice(4, 4));
    }

    /// <summary>The scalar reference of <see cref="HighPass"/>.</summary>
    internal static void HighPassScalar(ReadOnlySpan<float> plane, Span<float> output, int width, int height, CancellationToken cancellationToken)
    {
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            HighPassColumns(plane, output, width, height, y, 0, width);
        }
    }

    /// <summary>Filters the samples <paramref name="start"/> to <paramref name="end"/> (excluded) of the row <paramref name="y"/>, one at a time.</summary>
    private static void HighPassColumns(ReadOnlySpan<float> plane, Span<float> output, int width, int height, int y, int start, int end)
    {
        for (var x = start; x < end; x++)
        {
            var others = 0f;
            for (var dy = -2; dy <= 2; dy++)
            {
                var row = plane.Slice(Mirror(y + dy, height) * width, width);
                for (var dx = -2; dx <= 2; dx++)
                {
                    if (dx != 0 || dy != 0)
                    {
                        others += row[Mirror(x + dx, width)];
                    }
                }
            }

            output[(y * width) + x] = (others * 0.16f) + (plane[(y * width) + x] * -3.84f);
        }
    }

    private static float GetStrength(ReadOnlySpan<float> strengths, float intensity)
    {
        const int Scale = PointCount - 2;
        var scaled = MathF.Max(0, intensity * Scale);
        var floor = MathF.Floor(scaled);
        var fraction = scaled - floor;
        if (scaled >= Scale + 1)
        {
            floor = Scale;
            fraction = 1;
        }

        var index = (int)floor;
        var low = strengths[index];
        var value = ((strengths[index + 1] - low) * fraction) + low;
        return MathF.Min(1, MathF.Max(0, value));
    }

    private static void Seed(Span<ulong> state0, Span<ulong> state1, uint seed1, uint seed2, uint seed3, uint seed4)
    {
        state0[0] = SplitMix64((((ulong)seed1 << 32) + seed2) + 0x9E3779B97F4A7C15UL);
        state1[0] = SplitMix64((((ulong)seed3 << 32) + seed4) + 0x9E3779B97F4A7C15UL);
        for (var i = 1; i < LaneCount; i++)
        {
            state0[i] = SplitMix64(state0[i - 1]);
            state1[i] = SplitMix64(state1[i - 1]);
        }
    }

    private static ulong SplitMix64(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    /// <summary>Advances the eight lanes and turns their 64-bit outputs into 16 values of the range 1 to 2: the low half of an output, then its high half.</summary>
    private static void FillBatch(Span<ulong> state0, Span<ulong> state1, Span<float> batch)
    {
        for (var i = 0; i < LaneCount; i++)
        {
            var s1 = state0[i];
            var s0 = state1[i];
            var bits = unchecked(s1 + s0);
            state0[i] = s0;
            s1 ^= s1 << 23;
            s1 ^= s0 ^ (s1 >> 18) ^ (s0 >> 5);
            state1[i] = s1;
            batch[2 * i] = BitConverter.UInt32BitsToSingle(((uint)bits >> 9) | 0x3F800000);
            batch[(2 * i) + 1] = BitConverter.UInt32BitsToSingle(((uint)(bits >> 32) >> 9) | 0x3F800000);
        }
    }

    private static int Mirror(int position, int size)
    {
        while (position < 0 || position >= size)
        {
            position = position < 0 ? -position - 1 : (2 * size) - 1 - position;
        }

        return position;
    }
}
