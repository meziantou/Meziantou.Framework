using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The restoration filters applied to the three color channels of a frame before its color transform (ISO/IEC 18181-1,
/// annex J): the Gabor-like filter, a fixed 3x3 convolution that undoes the sharpening of the encoder, and the
/// edge-preserving filter, which averages each sample with the neighbors that look like it.
/// </summary>
/// <remarks>
/// The filters read the samples outside the frame as the mirror image of the samples inside, the edge sample included.
/// Each one reads a copy of the channels that has that border and writes the channels in place.
/// </remarks>
internal static class JpegXlRestorationFilters
{
    /// <summary>The numerator of the strength of the edge-preserving filter of a block, divided by its quantization.</summary>
    private const float InverseSigmaNumerator = -1.1715728752538099024f;

    /// <summary>The inverse strength below which the edge-preserving filter leaves a block as it is: a strength under 0.3.</summary>
    private const float MinInverseSigma = -3.90524291751269967465540850526868f;

    private const int Border = 3;

    /// <summary>The largest number of samples over which a neighbor is compared with a sample.</summary>
    private const int MaxPatchLength = 5;

    /// <summary>
    /// Computes, for each block of 8x8 samples of a VarDCT frame, the inverse of the strength of the edge-preserving filter:
    /// the strength grows with the quantization step of the block and with its sharpness parameter.
    /// </summary>
    /// <param name="filter">The parameters of the filter.</param>
    /// <param name="globalScale">The scale of the quantization of the frame.</param>
    /// <param name="quantField">The quantization factor of each block; a block that does not start a transform has the factor of its transform.</param>
    /// <param name="sharpness">The sharpness parameter of each block.</param>
    /// <param name="inverseSigma">Receives the inverse strength of each block.</param>
    public static void ComputeInverseSigma(JpegXlRestorationFilter filter, int globalScale, ReadOnlySpan<ushort> quantField, ReadOnlySpan<byte> sharpness, Span<float> inverseSigma)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var quantScale = globalScale * (1f / 65536);
        for (var i = 0; i < inverseSigma.Length; i++)
        {
            var sigma = filter.EpfQuantMultiplier / (quantScale * quantField[i] * InverseSigmaNumerator) * filter.EpfSharpnessLut[sharpness[i]];
            inverseSigma[i] = 1f / MathF.Min(-1e-4f, sigma);
        }
    }

    /// <summary>Gets the inverse of the strength of the edge-preserving filter of every block of a Modular frame, which has no quantization.</summary>
    public static float GetModularInverseSigma(JpegXlRestorationFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return InverseSigmaNumerator / filter.EpfSigmaForModular;
    }

    /// <summary>Applies the Gabor-like filter to the three channels.</summary>
    /// <param name="planes">The samples of each channel, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows.</param>
    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <param name="filter">The parameters of the filter.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void ApplyGaborish(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var paddedWidth = width + 2;
        using var padded = Av1PooledArray<float>.Rent(scope, (long)paddedWidth * (height + 2), clear: false);
        for (var c = 0; c < 3; c++)
        {
            // The weights of the center, of the four sides and of the four corners, normalized
            var side = filter.GaborWeights[c * 2];
            var corner = filter.GaborWeights[(c * 2) + 1];
            var scale = 1f / (1f + (4f * (side + corner)));
            var centerWeight = scale;
            var sideWeight = side * scale;
            var cornerWeight = corner * scale;
            var plane = planes[c].Span;
            CopyWithBorder(plane, stride, width, height, padded.Span, 1);
            for (var y = 0; y < height; y++)
            {
                if ((y & 63) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var top = padded.Span.Slice(y * paddedWidth, paddedWidth);
                var middle = padded.Span.Slice((y + 1) * paddedWidth, paddedWidth);
                var bottom = padded.Span.Slice((y + 2) * paddedWidth, paddedWidth);
                GaborishRow(top, middle, bottom, centerWeight, sideWeight, cornerWeight, plane.Slice(y * stride, width));
            }
        }
    }

    /// <summary>
    /// Filters one row with the Gabor-like filter, four samples at a time: each lane performs the operations of
    /// <see cref="GaborishRowScalar"/> in the same order.
    /// </summary>
    /// <param name="top">The row above, with one sample of border on each side.</param>
    /// <param name="middle">The row, with one sample of border on each side.</param>
    /// <param name="bottom">The row below, with one sample of border on each side.</param>
    /// <param name="centerWeight">The weight of the sample.</param>
    /// <param name="sideWeight">The weight of the four samples beside it.</param>
    /// <param name="cornerWeight">The weight of the four samples at its corners.</param>
    /// <param name="output">Receives the filtered samples.</param>
    internal static void GaborishRow(ReadOnlySpan<float> top, ReadOnlySpan<float> middle, ReadOnlySpan<float> bottom, float centerWeight, float sideWeight, float cornerWeight, Span<float> output)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var center = Vector128.Create(centerWeight);
            var side = Vector128.Create(sideWeight);
            var corner = Vector128.Create(cornerWeight);
            for (; x <= output.Length - 4; x += 4)
            {
                var sides = (Vector128.Create(middle.Slice(x, 4)) + Vector128.Create(middle.Slice(x + 2, 4))) + (Vector128.Create(top.Slice(x + 1, 4)) + Vector128.Create(bottom.Slice(x + 1, 4)));
                var corners = (Vector128.Create(top.Slice(x, 4)) + Vector128.Create(top.Slice(x + 2, 4))) + (Vector128.Create(bottom.Slice(x, 4)) + Vector128.Create(bottom.Slice(x + 2, 4)));
                ((corners * corner) + ((sides * side) + (Vector128.Create(middle.Slice(x + 1, 4)) * center))).CopyTo(output.Slice(x, 4));
            }
        }

        GaborishRowScalar(top, middle, bottom, centerWeight, sideWeight, cornerWeight, output, x);
    }

    /// <summary>The scalar reference of <see cref="GaborishRow"/>, from the sample <paramref name="start"/> on.</summary>
    internal static void GaborishRowScalar(ReadOnlySpan<float> top, ReadOnlySpan<float> middle, ReadOnlySpan<float> bottom, float centerWeight, float sideWeight, float cornerWeight, Span<float> output, int start = 0)
    {
        for (var x = start; x < output.Length; x++)
        {
            var sides = (middle[x] + middle[x + 2]) + (top[x + 1] + bottom[x + 1]);
            var corners = (top[x] + top[x + 2]) + (bottom[x] + bottom[x + 2]);
            output[x] = (corners * cornerWeight) + ((sides * sideWeight) + (middle[x + 1] * centerWeight));
        }
    }

    /// <summary>Applies the steps of the edge-preserving filter that the number of iterations of the frame selects.</summary>
    /// <param name="planes">The samples of each channel, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows.</param>
    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <param name="filter">The parameters of the filter.</param>
    /// <param name="inverseSigma">The inverse strength of each block of 8x8 samples, in rows of <paramref name="blocksWide"/> values.</param>
    /// <param name="blocksWide">The width of the frame, in blocks.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void ApplyEdgePreservingFilter(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, ReadOnlySpan<float> inverseSigma, int blocksWide, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.EpfIterations == 0)
            return;

        var paddedWidth = width + (2 * Border);
        var paddedLength = (long)paddedWidth * (height + (2 * Border));
        using var padded = Av1PooledArray<float>.Rent(scope, 3 * paddedLength, clear: false);

        // Three iterations add a first step with a wide neighborhood; two add a last step that compares single samples
        if (filter.EpfIterations >= 3)
        {
            ApplyStep(planes, stride, width, height, filter, inverseSigma, blocksWide, padded.Span, step: 0, cancellationToken);
        }

        ApplyStep(planes, stride, width, height, filter, inverseSigma, blocksWide, padded.Span, step: 1, cancellationToken);
        if (filter.EpfIterations >= 2)
        {
            ApplyStep(planes, stride, width, height, filter, inverseSigma, blocksWide, padded.Span, step: 2, cancellationToken);
        }
    }

    private static void ApplyStep(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, ReadOnlySpan<float> inverseSigma, int blocksWide, Span<float> padded, int step, CancellationToken cancellationToken)
    {
        // The neighbors that are averaged with a sample, as (row, column) offsets
        ReadOnlySpan<sbyte> wideNeighbors = [-2, 0, -1, -1, -1, 0, -1, 1, 0, -2, 0, -1, 0, 1, 0, 2, 1, -1, 1, 0, 1, 1, 2, 0];
        ReadOnlySpan<sbyte> crossNeighbors = [-1, 0, 0, -1, 0, 1, 1, 0];
        var neighbors = step == 0 ? wideNeighbors : crossNeighbors;

        // A neighbor is compared with the sample over a cross of five samples, or, in the last step, by itself
        ReadOnlySpan<sbyte> cross = [0, 0, -1, 0, 0, -1, 1, 0, 0, 1];
        var patch = step == 2 ? cross[..2] : cross;

        var paddedWidth = width + (2 * Border);
        var paddedLength = paddedWidth * (height + (2 * Border));
        var paddedX = padded[..paddedLength];
        var paddedY = padded.Slice(paddedLength, paddedLength);
        var paddedB = padded.Slice(2 * paddedLength, paddedLength);
        CopyWithBorder(planes[0].Span, stride, width, height, paddedX, Border);
        CopyWithBorder(planes[1].Span, stride, width, height, paddedY, Border);
        CopyWithBorder(planes[2].Span, stride, width, height, paddedB, Border);

        Span<int> neighborOffsets = stackalloc int[neighbors.Length / 2];
        for (var i = 0; i < neighborOffsets.Length; i++)
        {
            neighborOffsets[i] = (neighbors[i * 2] * paddedWidth) + neighbors[(i * 2) + 1];
        }

        Span<int> patchOffsets = stackalloc int[patch.Length / 2];
        for (var i = 0; i < patchOffsets.Length; i++)
        {
            patchOffsets[i] = (patch[i * 2] * paddedWidth) + patch[(i * 2) + 1];
        }

        // The differences count less on the border of a block of 8x8 samples, where the transforms leave discontinuities
        var sigmaScale = step switch
        {
            0 => filter.EpfPass0SigmaScale * 1.65f,
            1 => 1.65f,
            _ => filter.EpfPass2SigmaScale * 1.65f,
        };
        var parameters = new EdgePreservingStep(sigmaScale, sigmaScale * filter.EpfBorderSadMultiplier, filter.EpfChannelScale[0], filter.EpfChannelScale[1], filter.EpfChannelScale[2]);
        var outputX = planes[0].Span;
        var outputY = planes[1].Span;
        var outputB = planes[2].Span;
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var isBorderRow = (y & 7) is 0 or 7;
            var sigmaRow = inverseSigma.Slice((y >> 3) * blocksWide, blocksWide);
            var rowStart = ((y + Border) * paddedWidth) + Border;
            var target = y * stride;
            FilterRow(paddedX, paddedY, paddedB, rowStart, width, neighborOffsets, patchOffsets, sigmaRow, isBorderRow, parameters, outputX.Slice(target, width), outputY.Slice(target, width), outputB.Slice(target, width));
        }
    }

    /// <summary>
    /// Filters one row of samples with one step of the edge-preserving filter, four samples at a time: each lane performs
    /// the operations of <see cref="FilterRowScalar"/> in the same order, and a neighbor whose weight is not positive is left
    /// out by a selection, not by a product with zero.
    /// </summary>
    /// <param name="paddedX">The X channel with its border.</param>
    /// <param name="paddedY">The Y channel with its border.</param>
    /// <param name="paddedB">The B channel with its border.</param>
    /// <param name="rowStart">The position of the first sample of the row in the padded channels.</param>
    /// <param name="width">The number of samples of the row.</param>
    /// <param name="neighborOffsets">The offsets of the neighbors averaged with a sample.</param>
    /// <param name="patchOffsets">The offsets of the samples over which a neighbor is compared with the sample.</param>
    /// <param name="sigmaRow">The inverse strength of each block of the row.</param>
    /// <param name="isBorderRow">Whether the row is the first or the last of its blocks.</param>
    /// <param name="parameters">The scales of the step.</param>
    /// <param name="outputX">Receives the X samples of the row; the samples of a block the filter leaves are not written.</param>
    /// <param name="outputY">Receives the Y samples of the row.</param>
    /// <param name="outputB">Receives the B samples of the row.</param>
    internal static void FilterRow(
        ReadOnlySpan<float> paddedX,
        ReadOnlySpan<float> paddedY,
        ReadOnlySpan<float> paddedB,
        int rowStart,
        int width,
        ReadOnlySpan<int> neighborOffsets,
        ReadOnlySpan<int> patchOffsets,
        ReadOnlySpan<float> sigmaRow,
        bool isBorderRow,
        in EdgePreservingStep parameters,
        Span<float> outputX,
        Span<float> outputY,
        Span<float> outputB)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && patchOffsets.Length <= MaxPatchLength && IsInside(paddedX.Length, rowStart, width, neighborOffsets, patchOffsets) && IsInside(paddedY.Length, rowStart, width, neighborOffsets, patchOffsets) && IsInside(paddedB.Length, rowStart, width, neighborOffsets, patchOffsets))
        {
            // Every position read is checked above, once for the row: the loads are not checked again
            ref var originX = ref unsafe(MemoryMarshal.GetReference(paddedX));
            ref var originY = ref unsafe(MemoryMarshal.GetReference(paddedY));
            ref var originB = ref unsafe(MemoryMarshal.GetReference(paddedB));
            var scaleX = Vector128.Create(parameters.ScaleX);
            var scaleY = Vector128.Create(parameters.ScaleY);
            var scaleB = Vector128.Create(parameters.ScaleB);

            // The samples of the patch around the four samples, which every neighbor is compared with
            Span<Vector128<float>> patch = stackalloc Vector128<float>[3 * MaxPatchLength];
            var patchX = patch[..patchOffsets.Length];
            var patchY = patch.Slice(MaxPatchLength, patchOffsets.Length);
            var patchB = patch.Slice(2 * MaxPatchLength, patchOffsets.Length);
            for (; x <= width - 4; x += 4)
            {
                // The four samples are in the same block: the first one is on its left border, or the last one on its right border
                var blockInverseSigma = sigmaRow[x >> 3];
                if (blockInverseSigma < MinInverseSigma)
                    continue;

                var inner = blockInverseSigma * parameters.SigmaScale;
                var border = blockInverseSigma * parameters.BorderScale;
                var scaledInverseSigma = isBorderRow ? Vector128.Create(border) : (x & 4) == 0 ? Vector128.Create(border, inner, inner, inner) : Vector128.Create(inner, inner, inner, border);
                var position = rowStart + x;
                for (var k = 0; k < patchOffsets.Length; k++)
                {
                    var reference = position + patchOffsets[k];
                    patchX[k] = Load(ref originX, reference);
                    patchY[k] = Load(ref originY, reference);
                    patchB[k] = Load(ref originB, reference);
                }

                var weightSum = Vector128<float>.One;
                var sumX = Load(ref originX, position);
                var sumY = Load(ref originY, position);
                var sumB = Load(ref originB, position);
                foreach (var neighbor in neighborOffsets)
                {
                    var differenceX = Vector128<float>.Zero;
                    var differenceY = Vector128<float>.Zero;
                    var differenceB = Vector128<float>.Zero;
                    for (var k = 0; k < patchOffsets.Length; k++)
                    {
                        var reference = position + patchOffsets[k] + neighbor;
                        differenceX += Vector128.Abs(patchX[k] - Load(ref originX, reference));
                        differenceY += Vector128.Abs(patchY[k] - Load(ref originY, reference));
                        differenceB += Vector128.Abs(patchB[k] - Load(ref originB, reference));
                    }

                    var difference = (differenceX * scaleX) + (differenceY * scaleY) + (differenceB * scaleB);
                    var weight = (difference * scaledInverseSigma) + Vector128<float>.One;
                    var isPositive = Vector128.GreaterThan(weight, Vector128<float>.Zero);
                    weightSum = Vector128.ConditionalSelect(isPositive, weightSum + weight, weightSum);
                    sumX = Vector128.ConditionalSelect(isPositive, sumX + (weight * Load(ref originX, position + neighbor)), sumX);
                    sumY = Vector128.ConditionalSelect(isPositive, sumY + (weight * Load(ref originY, position + neighbor)), sumY);
                    sumB = Vector128.ConditionalSelect(isPositive, sumB + (weight * Load(ref originB, position + neighbor)), sumB);
                }

                var inverseWeight = Vector128<float>.One / weightSum;
                (sumX * inverseWeight).CopyTo(outputX.Slice(x, 4));
                (sumY * inverseWeight).CopyTo(outputY.Slice(x, 4));
                (sumB * inverseWeight).CopyTo(outputB.Slice(x, 4));
            }
        }

        FilterRowScalar(paddedX, paddedY, paddedB, rowStart, width, neighborOffsets, patchOffsets, sigmaRow, isBorderRow, parameters, outputX, outputY, outputB, x);
    }

    /// <summary>
    /// Gets a value indicating whether every position the vectorized row reads is inside a padded channel: from the first
    /// sample of the row plus the most negative offsets to the last one plus the most positive offsets.
    /// </summary>
    private static bool IsInside(int length, int rowStart, int width, ReadOnlySpan<int> neighborOffsets, ReadOnlySpan<int> patchOffsets)
    {
        int lowestNeighbor = 0, highestNeighbor = 0, lowestPatch = 0, highestPatch = 0;
        foreach (var offset in neighborOffsets)
        {
            lowestNeighbor = Math.Min(lowestNeighbor, offset);
            highestNeighbor = Math.Max(highestNeighbor, offset);
        }

        foreach (var offset in patchOffsets)
        {
            lowestPatch = Math.Min(lowestPatch, offset);
            highestPatch = Math.Max(highestPatch, offset);
        }

        return width > 0 && (long)rowStart + lowestNeighbor + lowestPatch >= 0 && (long)rowStart + width - 1 + highestNeighbor + highestPatch < length;
    }

    /// <summary>Loads four samples from a position that was checked.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Load(ref float origin, int position) => unsafe(Vector128.LoadUnsafe(ref origin, (nuint)position));

    /// <summary>The scalar reference of <see cref="FilterRow"/>, from the sample <paramref name="start"/> on.</summary>
    internal static void FilterRowScalar(
        ReadOnlySpan<float> paddedX,
        ReadOnlySpan<float> paddedY,
        ReadOnlySpan<float> paddedB,
        int rowStart,
        int width,
        ReadOnlySpan<int> neighborOffsets,
        ReadOnlySpan<int> patchOffsets,
        ReadOnlySpan<float> sigmaRow,
        bool isBorderRow,
        in EdgePreservingStep parameters,
        Span<float> outputX,
        Span<float> outputY,
        Span<float> outputB,
        int start = 0)
    {
        for (var x = start; x < width; x++)
        {
            var blockInverseSigma = sigmaRow[x >> 3];
            if (blockInverseSigma < MinInverseSigma)
                continue;

            var position = rowStart + x;
            var scaledInverseSigma = blockInverseSigma * (isBorderRow || (x & 7) is 0 or 7 ? parameters.BorderScale : parameters.SigmaScale);
            var weightSum = 1f;
            var sumX = paddedX[position];
            var sumY = paddedY[position];
            var sumB = paddedB[position];
            foreach (var neighbor in neighborOffsets)
            {
                var differenceX = 0f;
                var differenceY = 0f;
                var differenceB = 0f;
                foreach (var offset in patchOffsets)
                {
                    var reference = position + offset;
                    differenceX += MathF.Abs(paddedX[reference] - paddedX[reference + neighbor]);
                    differenceY += MathF.Abs(paddedY[reference] - paddedY[reference + neighbor]);
                    differenceB += MathF.Abs(paddedB[reference] - paddedB[reference + neighbor]);
                }

                var difference = (differenceX * parameters.ScaleX) + (differenceY * parameters.ScaleY) + (differenceB * parameters.ScaleB);
                var weight = (difference * scaledInverseSigma) + 1f;
                if (weight > 0)
                {
                    weightSum += weight;
                    sumX += weight * paddedX[position + neighbor];
                    sumY += weight * paddedY[position + neighbor];
                    sumB += weight * paddedB[position + neighbor];
                }
            }

            var inverseWeight = 1f / weightSum;
            outputX[x] = sumX * inverseWeight;
            outputY[x] = sumY * inverseWeight;
            outputB[x] = sumB * inverseWeight;
        }
    }

    /// <summary>The scales of one step of the edge-preserving filter.</summary>
    /// <param name="SigmaScale">The scale of the inverse strength inside a block.</param>
    /// <param name="BorderScale">The scale of the inverse strength on the border of a block.</param>
    /// <param name="ScaleX">The weight of the differences of the X channel.</param>
    /// <param name="ScaleY">The weight of the differences of the Y channel.</param>
    /// <param name="ScaleB">The weight of the differences of the B channel.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct EdgePreservingStep(float SigmaScale, float BorderScale, float ScaleX, float ScaleY, float ScaleB);

    /// <summary>Copies a channel with a border of mirrored samples around it.</summary>
    private static void CopyWithBorder(ReadOnlySpan<float> plane, int stride, int width, int height, Span<float> padded, int border)
    {
        var paddedWidth = width + (2 * border);
        for (var y = -border; y < height + border; y++)
        {
            var source = plane.Slice(Mirror(y, height) * stride, width);
            var target = padded.Slice((y + border) * paddedWidth, paddedWidth);
            source.CopyTo(target[border..]);
            for (var x = 1; x <= border; x++)
            {
                target[border - x] = source[Mirror(-x, width)];
                target[border + width - 1 + x] = source[Mirror(width - 1 + x, width)];
            }
        }
    }

    /// <summary>The position inside a side of a position that may be outside: the side is mirrored at its two ends, which are repeated.</summary>
    private static int Mirror(int position, int size)
    {
        while (position < 0 || position >= size)
        {
            position = position < 0 ? -position - 1 : (2 * size) - 1 - position;
        }

        return position;
    }
}
