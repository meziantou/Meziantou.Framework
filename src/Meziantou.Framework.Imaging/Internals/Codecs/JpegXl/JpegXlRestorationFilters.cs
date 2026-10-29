using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The restoration filters applied to the three color channels of a frame before its color transform (ISO/IEC 18181-1,
/// annex J): the Gabor-like filter, a fixed 3x3 convolution that undoes the sharpening of the encoder, and the
/// edge-preserving filter, which averages each sample with the neighbors that look like it.
/// </summary>
/// <remarks>
/// <para>
/// The filters read the samples outside the frame as the mirror image of the samples inside, the edge sample included.
/// </para>
/// <para>
/// They run as a pipeline over the rows of the frame: each filter (the Gabor-like filter, then each step of the
/// edge-preserving filter) keeps the rows it reads in a ring of 2 r + 1 rows with a border of r mirrored samples on each
/// side, where r is the reach of the filter (1, then 3, 2 and 1). It produces a row as soon as it holds the rows around
/// it, and writes it in the ring of the next filter; the last filter writes the channels in place, below the rows the
/// first one has already copied. The result is the one of the filters applied to the whole frame one after the other.
/// </para>
/// <para>
/// Several workers filter a large frame in bands of rows, each with its own pipeline. The first filter of a band receives
/// its rows and, around them, the rows up to the sum of the reaches of the filters, which the neighbor bands filter in
/// place: they are copied before any band starts. Each filter produces the rows of the band and the rows around them that
/// the filters after it read, so a band computes again a few rows of its neighbors, and the result does not depend on
/// the number of bands.
/// </para>
/// <para>
/// A step of the edge-preserving filter computes, once for each row, the absolute differences of the samples with their
/// neighbor in each direction of the step, then their sums over the patch: the distance of each sample to that neighbor.
/// The distance of a sample to its neighbor in the opposite direction is the distance of that neighbor, since the
/// absolute difference of two samples does not depend on their order; the sums keep the order of the patch.
/// </para>
/// </remarks>
internal static class JpegXlRestorationFilters
{
    /// <summary>The numerator of the strength of the edge-preserving filter of a block, divided by its quantization.</summary>
    private const float InverseSigmaNumerator = -1.1715728752538099024f;

    /// <summary>The inverse strength below which the edge-preserving filter leaves a block as it is: a strength under 0.3.</summary>
    private const float MinInverseSigma = -3.90524291751269967465540850526868f;

    /// <summary>The step of the pipeline that is the Gabor-like filter; the steps of the edge-preserving filter are 0 to 2.</summary>
    private const int GaborishStep = -1;

    /// <summary>The largest reach of a filter: the first step of the edge-preserving filter.</summary>
    private const int MaxBorder = 3;

    /// <summary>The largest number of samples over which a neighbor is compared with a sample.</summary>
    private const int MaxPatchLength = 5;

    /// <summary>The largest number of neighbors averaged with a sample.</summary>
    private const int MaxNeighbors = 12;

    /// <summary>Gets the neighbors that the first step averages with a sample, as (row, column) offsets.</summary>
    private static ReadOnlySpan<sbyte> WideNeighbors => [-2, 0, -1, -1, -1, 0, -1, 1, 0, -2, 0, -1, 0, 1, 0, 2, 1, -1, 1, 0, 1, 1, 2, 0];

    /// <summary>Gets the neighbors that the other steps average with a sample.</summary>
    private static ReadOnlySpan<sbyte> CrossNeighbors => [-1, 0, 0, -1, 0, 1, 1, 0];

    /// <summary>Gets the samples over which a neighbor is compared with the sample: a cross of five, or, in the last step, the sample itself.</summary>
    private static ReadOnlySpan<sbyte> Cross => [0, 0, -1, 0, 0, -1, 1, 0, 0, 1];

    /// <summary>
    /// Gets the directions whose absolute differences the first step computes once per row: every neighbor is one of them
    /// or its opposite, and the difference of a sample with its neighbor in the opposite direction is the difference
    /// computed for that neighbor.
    /// </summary>
    private static ReadOnlySpan<sbyte> WideDirections => [2, 0, 1, 1, 1, 0, 1, -1, 0, 2, 0, 1];

    /// <summary>Gets the directions whose absolute differences the other steps compute once per row.</summary>
    private static ReadOnlySpan<sbyte> CrossDirections => [1, 0, 0, 1];

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

    /// <summary>
    /// Gets the number of bands of rows a frame is filtered in: one per worker, at most
    /// <paramref name="maxDegreeOfParallelism"/>, and one for a small frame (<see cref="RowBands.GetWorkerCount"/>).
    /// </summary>
    public static int GetBandCount(int width, int height, int maxDegreeOfParallelism) => RowBands.GetWorkerCount(new Size(width, height), maxDegreeOfParallelism);

    /// <summary>Applies the Gabor-like filter to the three channels.</summary>
    /// <param name="planes">The samples of each channel, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows.</param>
    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <param name="filter">The parameters of the filter.</param>
    /// <param name="bands">The number of bands of rows filtered at the same time (<see cref="GetBandCount"/>); the result does not depend on it.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void ApplyGaborish(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, int bands, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Run(planes, stride, width, height, filter, isGaborEnabled: true, epfIterations: 0, inverseSigma: default, blocksWide: 0, bands, scope, cancellationToken);
    }

    /// <summary>Applies the steps of the edge-preserving filter that the number of iterations of the frame selects.</summary>
    /// <param name="planes">The samples of each channel, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows.</param>
    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <param name="filter">The parameters of the filter.</param>
    /// <param name="inverseSigma">The inverse strength of each block of 8x8 samples, in rows of <paramref name="blocksWide"/> values.</param>
    /// <param name="blocksWide">The width of the frame, in blocks.</param>
    /// <param name="bands">The number of bands of rows filtered at the same time (<see cref="GetBandCount"/>); the result does not depend on it.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void ApplyEdgePreservingFilter(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, ReadOnlySpan<float> inverseSigma, int blocksWide, int bands, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Run(planes, stride, width, height, filter, isGaborEnabled: false, filter.EpfIterations, inverseSigma, blocksWide, bands, scope, cancellationToken);
    }

    /// <summary>
    /// Applies the Gabor-like filter if the frame enables it, then the steps of the edge-preserving filter, in one pass over
    /// the rows.
    /// </summary>
    /// <param name="planes">The samples of each channel, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows.</param>
    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <param name="filter">The parameters of the filters.</param>
    /// <param name="inverseSigma">The inverse strength of each block of 8x8 samples, in rows of <paramref name="blocksWide"/> values; not read without edge-preserving filter.</param>
    /// <param name="blocksWide">The width of the frame, in blocks.</param>
    /// <param name="bands">The number of bands of rows filtered at the same time (<see cref="GetBandCount"/>); the result does not depend on it.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void Apply(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, ReadOnlySpan<float> inverseSigma, int blocksWide, int bands, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Run(planes, stride, width, height, filter, filter.IsGaborEnabled, filter.EpfIterations, inverseSigma, blocksWide, bands, scope, cancellationToken);
    }

    private static void Run(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, bool isGaborEnabled, int epfIterations, ReadOnlySpan<float> inverseSigma, int blocksWide, int bands, AllocationScope scope, CancellationToken cancellationToken)
    {
        // Three iterations add a first step with a wide neighborhood; two add a last step that compares single samples
        var steps = new List<int>(4);
        if (isGaborEnabled)
        {
            steps.Add(GaborishStep);
        }

        if (epfIterations >= 3)
        {
            steps.Add(0);
        }

        if (epfIterations >= 1)
        {
            steps.Add(1);
        }

        if (epfIterations >= 2)
        {
            steps.Add(2);
        }

        if (steps.Count == 0 || width <= 0 || height <= 0)
            return;

        bands = Math.Clamp(bands, 1, height);
        if (bands == 1)
        {
            RunBand(planes, stride, width, height, filter, CollectionsMarshal.AsSpan(steps), inverseSigma, blocksWide, 0, height, halo: default, scope, cancellationToken);
            return;
        }

        // A band reads the rows around it, up to the reach of the filters, which the neighbor bands filter in place: they
        // are copied before any band starts, and each band computes again the rows of the filters it needs around it
        var reach = 0;
        foreach (var step in steps)
        {
            reach += GetBorder(step);
        }

        var haloStarts = new int[bands + 1];
        for (var band = 0; band < bands; band++)
        {
            var (start, end) = GetBand(height, band, bands);
            haloStarts[band + 1] = checked(haloStarts[band] + (3 * width * GetHaloRows(height, reach, start, end)));
        }

        using var halo = Av1PooledArray<float>.Rent(scope, haloStarts[bands], clear: false);
        for (var band = 0; band < bands; band++)
        {
            var (start, end) = GetBand(height, band, bands);
            CopyHalo(planes, stride, width, height, reach, start, end, halo.Span[haloStarts[band]..haloStarts[band + 1]]);
        }

        // The workers cannot hold spans: they read the planes and the strengths of the blocks from arrays
        Av1PooledArray<float>[] planeArray = [planes[0], planes[1], planes[2]];
        using var sigma = inverseSigma.IsEmpty ? null : Av1PooledArray<float>.Rent(scope, inverseSigma.Length, clear: false);
        if (sigma is not null)
        {
            inverseSigma.CopyTo(sigma.Span);
        }

        RowBands.RunParallel(bands, band =>
        {
            var (start, end) = GetBand(height, band, bands);
            var sigmaSpan = sigma is null ? default : sigma.Span;
            RunBand(planeArray, stride, width, height, filter, CollectionsMarshal.AsSpan(steps), sigmaSpan, blocksWide, start, end, halo.Span[haloStarts[band]..haloStarts[band + 1]], scope, cancellationToken);
        });
    }

    /// <summary>Gets the rows of a band: the first one and the one after the last one.</summary>
    private static (int Start, int End) GetBand(int height, int band, int bands) => (RowBands.GetBandStart(height, band, bands), RowBands.GetBandStart(height, band + 1, bands));

    /// <summary>Gets the reach of a filter: the number of rows it reads above and below a row, and of samples on each side.</summary>
    private static int GetBorder(int step) => step switch
    {
        GaborishStep => 1,
        0 => 3,
        1 => 2,
        _ => 1,
    };

    /// <summary>Gets the number of rows of the frame that a band reads above and below its own rows.</summary>
    private static int GetHaloRows(int height, int reach, int start, int end) => (start - Math.Max(0, start - reach)) + (Math.Min(height, end + reach) - end);

    /// <summary>Copies the rows a band reads above and below its own rows, the three channels one after the other.</summary>
    private static void CopyHalo(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, int reach, int start, int end, Span<float> halo)
    {
        var first = Math.Max(0, start - reach);
        var last = Math.Min(height, end + reach);
        var rows = GetHaloRows(height, reach, start, end);
        for (var c = 0; c < 3; c++)
        {
            var plane = planes[c].Span;
            var target = halo.Slice(c * rows * width, rows * width);
            var index = 0;
            for (var y = first; y < last; y++)
            {
                if (y >= start && y < end)
                    continue;

                plane.Slice(y * stride, width).CopyTo(target.Slice(index * width, width));
                index++;
            }
        }
    }

    /// <summary>
    /// Filters the rows from <paramref name="start"/> to <paramref name="end"/>: the first filter receives them with the rows
    /// around them up to the reach of the filters, read from <paramref name="halo"/> (<see cref="CopyHalo"/>, empty for a band
    /// of the whole frame), and each filter produces the rows the next filters read.
    /// </summary>
    private static void RunBand(ReadOnlySpan<Av1PooledArray<float>> planes, int stride, int width, int height, JpegXlRestorationFilter filter, ReadOnlySpan<int> steps, ReadOnlySpan<float> inverseSigma, int blocksWide, int start, int end, ReadOnlySpan<float> halo, AllocationScope scope, CancellationToken cancellationToken)
    {
        // A filter produces the rows of the band and, around them, the rows the filters after it read
        Span<int> outputStarts = stackalloc int[steps.Length];
        Span<int> outputEnds = stackalloc int[steps.Length];
        var reach = 0;
        for (var s = steps.Length - 1; s >= 0; s--)
        {
            outputStarts[s] = Math.Max(0, start - reach);
            outputEnds[s] = Math.Min(height, end + reach);
            reach += GetBorder(steps[s]);
        }

        Span<Stage> stages = stackalloc Stage[steps.Length];
        var length = 0L;
        for (var s = 0; s < steps.Length; s++)
        {
            stages[s] = new Stage(steps[s], width, length, outputStarts[s], outputEnds[s]);
            length += stages[s].Length;
        }

        var first = Math.Max(0, start - reach);
        var last = Math.Min(height, end + reach);
        using var buffer = Av1PooledArray<float>.Rent(scope, length, clear: false);
        var pipeline = new Pipeline(planes[0].Span, planes[1].Span, planes[2].Span, stride, width, height, buffer.Span, stages, filter, inverseSigma, blocksWide, start, end, first, halo);
        for (var y = first; y < last; y++)
        {
            if (((y - first) & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            pipeline.Receive(y);
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

    /// <summary>Gets the neighbors that a step of the edge-preserving filter averages with a sample, as (row, column) offsets.</summary>
    internal static ReadOnlySpan<sbyte> GetNeighbors(int step) => step == 0 ? WideNeighbors : CrossNeighbors;

    /// <summary>Gets the samples over which a step of the edge-preserving filter compares a neighbor with the sample, as (row, column) offsets.</summary>
    internal static ReadOnlySpan<sbyte> GetPatch(int step) => step == 2 ? Cross[..2] : Cross;

    /// <summary>Gets the directions whose absolute differences a step of the edge-preserving filter computes once per row, as (row, column) offsets.</summary>
    internal static ReadOnlySpan<sbyte> GetDirections(int step) => step == 0 ? WideDirections : CrossDirections;

    /// <summary>
    /// Computes the absolute difference of each sample of <paramref name="first"/> with the sample of
    /// <paramref name="second"/> at the same position: <c>|first - second|</c>, the term of the distance between a sample
    /// and a neighbor.
    /// </summary>
    internal static void AbsoluteDifferences(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> differences)
    {
        var length = differences.Length;
        if (first.Length < length || second.Length < length)
            throw new ArgumentOutOfRangeException(nameof(differences));

        ref var a = ref unsafe(MemoryMarshal.GetReference(first));
        ref var b = ref unsafe(MemoryMarshal.GetReference(second));
        ref var target = ref unsafe(MemoryMarshal.GetReference(differences));
        var x = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            for (; x <= length - 8; x += 8)
            {
                unsafe
                {
                    Vector256.Abs(Vector256.LoadUnsafe(ref a, (nuint)x) - Vector256.LoadUnsafe(ref b, (nuint)x)).StoreUnsafe(ref target, (nuint)x);
                }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; x <= length - 4; x += 4)
            {
                unsafe
                {
                    Vector128.Abs(Vector128.LoadUnsafe(ref a, (nuint)x) - Vector128.LoadUnsafe(ref b, (nuint)x)).StoreUnsafe(ref target, (nuint)x);
                }
            }
        }

        for (; x < length; x++)
        {
            differences[x] = MathF.Abs(first[x] - second[x]);
        }
    }

    /// <summary>
    /// Computes the distances of a row of samples to their neighbors in one direction: for each sample, the sum of the
    /// absolute differences of the samples of its patch with their neighbor, from zero, in the order of the patch, the
    /// operations of <see cref="FilterRowScalar"/>.
    /// </summary>
    /// <param name="differences">The rows of absolute differences.</param>
    /// <param name="terms">For each sample of the patch, the position of its difference in <paramref name="differences"/>, for the sample at column 0.</param>
    /// <param name="start">The column of the first distance.</param>
    /// <param name="distances">Receives the distances.</param>
    internal static void SumPatch(ReadOnlySpan<float> differences, ReadOnlySpan<int> terms, int start, Span<float> distances)
    {
        var count = distances.Length;
        foreach (var term in terms)
        {
            if (term + start < 0 || (long)term + start + count > differences.Length)
                throw new ArgumentOutOfRangeException(nameof(terms));
        }

        var x = 0;
        ref var target = ref unsafe(MemoryMarshal.GetReference(distances));
        if (Vector128.IsHardwareAccelerated && terms.Length is 1 or MaxPatchLength)
        {
            // The positions are checked above, once for the row
            ref var source = ref unsafe(MemoryMarshal.GetReference(differences));
            ref var d0 = ref unsafe(Unsafe.Add(ref source, terms[0] + start));
            if (terms.Length == 1)
            {
                for (; x <= count - 4; x += 4)
                {
                    unsafe
                    {
                        (Vector128<float>.Zero + Vector128.LoadUnsafe(ref d0, (nuint)x)).StoreUnsafe(ref target, (nuint)x);
                    }
                }
            }
            else
            {
                ref var d1 = ref unsafe(Unsafe.Add(ref source, terms[1] + start));
                ref var d2 = ref unsafe(Unsafe.Add(ref source, terms[2] + start));
                ref var d3 = ref unsafe(Unsafe.Add(ref source, terms[3] + start));
                ref var d4 = ref unsafe(Unsafe.Add(ref source, terms[4] + start));
                if (Vector256.IsHardwareAccelerated)
                {
                    for (; x <= count - 8; x += 8)
                    {
                        unsafe
                        {
                            var i = (nuint)x;
                            var sum = ((((Vector256<float>.Zero + Vector256.LoadUnsafe(ref d0, i)) + Vector256.LoadUnsafe(ref d1, i)) + Vector256.LoadUnsafe(ref d2, i)) + Vector256.LoadUnsafe(ref d3, i)) + Vector256.LoadUnsafe(ref d4, i);
                            sum.StoreUnsafe(ref target, i);
                        }
                    }
                }

                for (; x <= count - 4; x += 4)
                {
                    unsafe
                    {
                        var i = (nuint)x;
                        var sum = ((((Vector128<float>.Zero + Vector128.LoadUnsafe(ref d0, i)) + Vector128.LoadUnsafe(ref d1, i)) + Vector128.LoadUnsafe(ref d2, i)) + Vector128.LoadUnsafe(ref d3, i)) + Vector128.LoadUnsafe(ref d4, i);
                        sum.StoreUnsafe(ref target, i);
                    }
                }
            }
        }

        for (; x < count; x++)
        {
            var sum = 0f;
            foreach (var term in terms)
            {
                sum += differences[term + start + x];
            }

            distances[x] = sum;
        }
    }

    /// <summary>
    /// Filters one row of samples with one step of the edge-preserving filter, four or eight samples at a time: each lane
    /// performs the operations of <see cref="FilterRowScalar"/> in the same order, and a neighbor whose weight is not
    /// positive is left out by a selection, not by a product with zero. The distance of a sample to each neighbor is read
    /// from <see cref="EdgePreservingRow.Distances"/>, computed once for the rows (<see cref="SumPatch"/>).
    /// </summary>
    /// <param name="row">The samples, their distances to their neighbors and the parameters of the row.</param>
    /// <param name="outputX">Receives the X samples of the row; the samples of a block the filter leaves are not written.</param>
    /// <param name="outputY">Receives the Y samples of the row.</param>
    /// <param name="outputB">Receives the B samples of the row.</param>
    internal static void FilterRow(in EdgePreservingRow row, Span<float> outputX, Span<float> outputY, Span<float> outputB)
    {
        var width = outputX.Length;
        var x = 0;
        if (Vector128.IsHardwareAccelerated && IsInside(row, width, outputY.Length, outputB.Length))
        {
            // Eight samples at a time share the computation of the positions they read: with two vectors of four when
            // there is no vector of eight
            x = Vector256.IsHardwareAccelerated ? FilterVector256(row, outputX, outputY, outputB) : FilterVector128Pairs(row, outputX, outputY, outputB);
            x = FilterVector128(row, outputX, outputY, outputB, x);
        }

        FilterRowScalar(row, outputX, outputY, outputB, x);
    }

    /// <summary>
    /// Gets a value indicating whether every position the vectorized row reads is inside its spans, from the first sample
    /// of the row to the last one, in the three channels.
    /// </summary>
    private static bool IsInside(in EdgePreservingRow row, int width, int widthY, int widthB)
    {
        var neighbors = row.Neighbors;
        var neighborCount = neighbors.Length / 2;
        if (width < 4 || widthY < width || widthB < width || neighborCount > MaxNeighbors || row.DistanceOffsets.Length < neighborCount || row.SigmaRow.Length < (width + 7) / 8)
            return false;

        var border = (row.RowOffsets.Length - 1) / 2;
        var lastSample = (2L * row.ChannelLength) + width;
        if (!IsInRange(row.RowOffsets[border], lastSample, row.Samples.Length))
            return false;

        for (var i = 0; i < neighborCount; i++)
        {
            var rowIndex = border + neighbors[i * 2];
            if ((uint)rowIndex >= (uint)row.RowOffsets.Length || !IsInRange(row.RowOffsets[rowIndex] + neighbors[(i * 2) + 1], lastSample, row.Samples.Length))
                return false;
        }

        var lastDistance = (2L * row.DistanceChannelLength) + width;
        foreach (var offset in row.DistanceOffsets[..neighborCount])
        {
            if (!IsInRange(offset, lastDistance, row.Distances.Length))
                return false;
        }

        return true;

        static bool IsInRange(int offset, long end, int length) => offset >= 0 && offset + end <= length;
    }

    /// <summary>The loop of <see cref="FilterRow"/> on blocks of eight samples, from the first sample of the row; returns the first sample it did not filter.</summary>
    private static int FilterVector256(in EdgePreservingRow row, Span<float> outputX, Span<float> outputY, Span<float> outputB)
    {
        // Every position read or written is checked by IsInside, once for the row: the loads are not checked again
        ref var samples = ref unsafe(MemoryMarshal.GetReference(row.Samples));
        ref var distances = ref unsafe(MemoryMarshal.GetReference(row.Distances));
        ref var targetX = ref unsafe(MemoryMarshal.GetReference(outputX));
        ref var targetY = ref unsafe(MemoryMarshal.GetReference(outputY));
        ref var targetB = ref unsafe(MemoryMarshal.GetReference(outputB));
        var channelLength = (nuint)row.ChannelLength;
        var distanceChannelLength = (nuint)row.DistanceChannelLength;
        var parameters = row.Parameters;
        var scaleX = Vector256.Create(parameters.ScaleX);
        var scaleY = Vector256.Create(parameters.ScaleY);
        var scaleB = Vector256.Create(parameters.ScaleB);
        var neighborCount = row.Neighbors.Length / 2;
        var distanceOffsets = row.DistanceOffsets;
        Span<int> neighborOffsets = stackalloc int[MaxNeighbors];
        var center = GetNeighborOffsets(row, neighborOffsets);
        var width = outputX.Length;
        var x = 0;
        for (; x <= width - 8; x += 8)
        {
            // The eight samples are a row of a block, from its left border to its right border
            var blockInverseSigma = row.SigmaRow[x >> 3];
            if (blockInverseSigma < MinInverseSigma)
                continue;

            var inner = blockInverseSigma * parameters.SigmaScale;
            var border = blockInverseSigma * parameters.BorderScale;
            var scaledInverseSigma = row.IsBorderRow ? Vector256.Create(border) : Vector256.Create(border, inner, inner, inner, inner, inner, inner, border);
            var position = (nuint)(center + x);
            var weightSum = Vector256<float>.One;
            Vector256<float> sumX, sumY, sumB;
            unsafe
            {
                sumX = Vector256.LoadUnsafe(ref samples, position);
                sumY = Vector256.LoadUnsafe(ref samples, position + channelLength);
                sumB = Vector256.LoadUnsafe(ref samples, position + (2 * channelLength));
            }

            for (var i = 0; i < neighborCount; i++)
            {
                var reference = (nuint)(distanceOffsets[i] + x);
                Vector256<float> differenceX, differenceY, differenceB;
                unsafe
                {
                    differenceX = Vector256.LoadUnsafe(ref distances, reference);
                    differenceY = Vector256.LoadUnsafe(ref distances, reference + distanceChannelLength);
                    differenceB = Vector256.LoadUnsafe(ref distances, reference + (2 * distanceChannelLength));
                }

                var difference = (differenceX * scaleX) + (differenceY * scaleY) + (differenceB * scaleB);
                var weight = (difference * scaledInverseSigma) + Vector256<float>.One;
                var isPositive = Vector256.GreaterThan(weight, Vector256<float>.Zero);
                var neighbor = (nuint)(neighborOffsets[i] + x);
                Vector256<float> neighborX, neighborY, neighborB;
                unsafe
                {
                    neighborX = Vector256.LoadUnsafe(ref samples, neighbor);
                    neighborY = Vector256.LoadUnsafe(ref samples, neighbor + channelLength);
                    neighborB = Vector256.LoadUnsafe(ref samples, neighbor + (2 * channelLength));
                }

                weightSum = Vector256.ConditionalSelect(isPositive, weightSum + weight, weightSum);
                sumX = Vector256.ConditionalSelect(isPositive, sumX + (weight * neighborX), sumX);
                sumY = Vector256.ConditionalSelect(isPositive, sumY + (weight * neighborY), sumY);
                sumB = Vector256.ConditionalSelect(isPositive, sumB + (weight * neighborB), sumB);
            }

            var inverseWeight = Vector256<float>.One / weightSum;
            unsafe
            {
                (sumX * inverseWeight).StoreUnsafe(ref targetX, (nuint)x);
                (sumY * inverseWeight).StoreUnsafe(ref targetY, (nuint)x);
                (sumB * inverseWeight).StoreUnsafe(ref targetB, (nuint)x);
            }
        }

        return x;
    }

    /// <summary>The loop of <see cref="FilterRow"/> on blocks of eight samples as two vectors of four, from the first sample of the row; returns the first sample it did not filter.</summary>
    private static int FilterVector128Pairs(in EdgePreservingRow row, Span<float> outputX, Span<float> outputY, Span<float> outputB)
    {
        // Every position read or written is checked by IsInside, once for the row: the loads are not checked again
        ref var samples = ref unsafe(MemoryMarshal.GetReference(row.Samples));
        ref var distances = ref unsafe(MemoryMarshal.GetReference(row.Distances));
        ref var targetX = ref unsafe(MemoryMarshal.GetReference(outputX));
        ref var targetY = ref unsafe(MemoryMarshal.GetReference(outputY));
        ref var targetB = ref unsafe(MemoryMarshal.GetReference(outputB));
        var channelLength = (nuint)row.ChannelLength;
        var distanceChannelLength = (nuint)row.DistanceChannelLength;
        var parameters = row.Parameters;
        var scaleX = Vector128.Create(parameters.ScaleX);
        var scaleY = Vector128.Create(parameters.ScaleY);
        var scaleB = Vector128.Create(parameters.ScaleB);
        var neighborCount = row.Neighbors.Length / 2;
        var distanceOffsets = row.DistanceOffsets;
        Span<int> neighborOffsets = stackalloc int[MaxNeighbors];
        var center = GetNeighborOffsets(row, neighborOffsets);
        var width = outputX.Length;
        var x = 0;
        for (; x <= width - 8; x += 8)
        {
            // The eight samples are a row of a block, from its left border to its right border
            var blockInverseSigma = row.SigmaRow[x >> 3];
            if (blockInverseSigma < MinInverseSigma)
                continue;

            var inner = blockInverseSigma * parameters.SigmaScale;
            var border = blockInverseSigma * parameters.BorderScale;
            var scaledInverseSigma0 = row.IsBorderRow ? Vector128.Create(border) : Vector128.Create(border, inner, inner, inner);
            var scaledInverseSigma1 = row.IsBorderRow ? Vector128.Create(border) : Vector128.Create(inner, inner, inner, border);
            var position = (nuint)(center + x);
            var weightSum0 = Vector128<float>.One;
            var weightSum1 = Vector128<float>.One;
            Vector128<float> sumX0, sumY0, sumB0, sumX1, sumY1, sumB1;
            unsafe
            {
                sumX0 = Vector128.LoadUnsafe(ref samples, position);
                sumY0 = Vector128.LoadUnsafe(ref samples, position + channelLength);
                sumB0 = Vector128.LoadUnsafe(ref samples, position + (2 * channelLength));
                sumX1 = Vector128.LoadUnsafe(ref samples, position + 4);
                sumY1 = Vector128.LoadUnsafe(ref samples, position + channelLength + 4);
                sumB1 = Vector128.LoadUnsafe(ref samples, position + (2 * channelLength) + 4);
            }

            for (var i = 0; i < neighborCount; i++)
            {
                ref var distance = ref unsafe(Unsafe.Add(ref distances, distanceOffsets[i] + x));
                ref var neighbor = ref unsafe(Unsafe.Add(ref samples, neighborOffsets[i] + x));
                Vector128<float> difference0, difference1, neighborX0, neighborY0, neighborB0, neighborX1, neighborY1, neighborB1;
                unsafe
                {
                    difference0 = (Vector128.LoadUnsafe(ref distance) * scaleX) + (Vector128.LoadUnsafe(ref distance, distanceChannelLength) * scaleY) + (Vector128.LoadUnsafe(ref distance, 2 * distanceChannelLength) * scaleB);
                    difference1 = (Vector128.LoadUnsafe(ref distance, 4) * scaleX) + (Vector128.LoadUnsafe(ref distance, distanceChannelLength + 4) * scaleY) + (Vector128.LoadUnsafe(ref distance, (2 * distanceChannelLength) + 4) * scaleB);
                    neighborX0 = Vector128.LoadUnsafe(ref neighbor);
                    neighborY0 = Vector128.LoadUnsafe(ref neighbor, channelLength);
                    neighborB0 = Vector128.LoadUnsafe(ref neighbor, 2 * channelLength);
                    neighborX1 = Vector128.LoadUnsafe(ref neighbor, 4);
                    neighborY1 = Vector128.LoadUnsafe(ref neighbor, channelLength + 4);
                    neighborB1 = Vector128.LoadUnsafe(ref neighbor, (2 * channelLength) + 4);
                }

                var weight0 = (difference0 * scaledInverseSigma0) + Vector128<float>.One;
                var weight1 = (difference1 * scaledInverseSigma1) + Vector128<float>.One;
                var isPositive0 = Vector128.GreaterThan(weight0, Vector128<float>.Zero);
                var isPositive1 = Vector128.GreaterThan(weight1, Vector128<float>.Zero);
                weightSum0 = Vector128.ConditionalSelect(isPositive0, weightSum0 + weight0, weightSum0);
                weightSum1 = Vector128.ConditionalSelect(isPositive1, weightSum1 + weight1, weightSum1);
                sumX0 = Vector128.ConditionalSelect(isPositive0, sumX0 + (weight0 * neighborX0), sumX0);
                sumX1 = Vector128.ConditionalSelect(isPositive1, sumX1 + (weight1 * neighborX1), sumX1);
                sumY0 = Vector128.ConditionalSelect(isPositive0, sumY0 + (weight0 * neighborY0), sumY0);
                sumY1 = Vector128.ConditionalSelect(isPositive1, sumY1 + (weight1 * neighborY1), sumY1);
                sumB0 = Vector128.ConditionalSelect(isPositive0, sumB0 + (weight0 * neighborB0), sumB0);
                sumB1 = Vector128.ConditionalSelect(isPositive1, sumB1 + (weight1 * neighborB1), sumB1);
            }

            var inverseWeight0 = Vector128<float>.One / weightSum0;
            var inverseWeight1 = Vector128<float>.One / weightSum1;
            unsafe
            {
                (sumX0 * inverseWeight0).StoreUnsafe(ref targetX, (nuint)x);
                (sumY0 * inverseWeight0).StoreUnsafe(ref targetY, (nuint)x);
                (sumB0 * inverseWeight0).StoreUnsafe(ref targetB, (nuint)x);
                (sumX1 * inverseWeight1).StoreUnsafe(ref targetX, (nuint)x + 4);
                (sumY1 * inverseWeight1).StoreUnsafe(ref targetY, (nuint)x + 4);
                (sumB1 * inverseWeight1).StoreUnsafe(ref targetB, (nuint)x + 4);
            }
        }

        return x;
    }

    /// <summary>The loop of <see cref="FilterRow"/> on four samples at a time, from the sample <paramref name="start"/>, a multiple of 4; returns the first sample it did not filter.</summary>
    private static int FilterVector128(in EdgePreservingRow row, Span<float> outputX, Span<float> outputY, Span<float> outputB, int start)
    {
        // Every position read or written is checked by IsInside, once for the row: the loads are not checked again
        ref var samples = ref unsafe(MemoryMarshal.GetReference(row.Samples));
        ref var distances = ref unsafe(MemoryMarshal.GetReference(row.Distances));
        ref var targetX = ref unsafe(MemoryMarshal.GetReference(outputX));
        ref var targetY = ref unsafe(MemoryMarshal.GetReference(outputY));
        ref var targetB = ref unsafe(MemoryMarshal.GetReference(outputB));
        var channelLength = (nuint)row.ChannelLength;
        var distanceChannelLength = (nuint)row.DistanceChannelLength;
        var parameters = row.Parameters;
        var scaleX = Vector128.Create(parameters.ScaleX);
        var scaleY = Vector128.Create(parameters.ScaleY);
        var scaleB = Vector128.Create(parameters.ScaleB);
        var neighborCount = row.Neighbors.Length / 2;
        var distanceOffsets = row.DistanceOffsets;
        Span<int> neighborOffsets = stackalloc int[MaxNeighbors];
        var center = GetNeighborOffsets(row, neighborOffsets);
        var width = outputX.Length;
        var x = start;
        for (; x <= width - 4; x += 4)
        {
            // The four samples are in the same block: the first one is on its left border, or the last one on its right border
            var blockInverseSigma = row.SigmaRow[x >> 3];
            if (blockInverseSigma < MinInverseSigma)
                continue;

            var inner = blockInverseSigma * parameters.SigmaScale;
            var border = blockInverseSigma * parameters.BorderScale;
            var scaledInverseSigma = row.IsBorderRow ? Vector128.Create(border) : (x & 4) == 0 ? Vector128.Create(border, inner, inner, inner) : Vector128.Create(inner, inner, inner, border);
            var position = (nuint)(center + x);
            var weightSum = Vector128<float>.One;
            Vector128<float> sumX, sumY, sumB;
            unsafe
            {
                sumX = Vector128.LoadUnsafe(ref samples, position);
                sumY = Vector128.LoadUnsafe(ref samples, position + channelLength);
                sumB = Vector128.LoadUnsafe(ref samples, position + (2 * channelLength));
            }

            for (var i = 0; i < neighborCount; i++)
            {
                var reference = (nuint)(distanceOffsets[i] + x);
                Vector128<float> differenceX, differenceY, differenceB;
                unsafe
                {
                    differenceX = Vector128.LoadUnsafe(ref distances, reference);
                    differenceY = Vector128.LoadUnsafe(ref distances, reference + distanceChannelLength);
                    differenceB = Vector128.LoadUnsafe(ref distances, reference + (2 * distanceChannelLength));
                }

                var difference = (differenceX * scaleX) + (differenceY * scaleY) + (differenceB * scaleB);
                var weight = (difference * scaledInverseSigma) + Vector128<float>.One;
                var isPositive = Vector128.GreaterThan(weight, Vector128<float>.Zero);
                var neighbor = (nuint)(neighborOffsets[i] + x);
                Vector128<float> neighborX, neighborY, neighborB;
                unsafe
                {
                    neighborX = Vector128.LoadUnsafe(ref samples, neighbor);
                    neighborY = Vector128.LoadUnsafe(ref samples, neighbor + channelLength);
                    neighborB = Vector128.LoadUnsafe(ref samples, neighbor + (2 * channelLength));
                }

                weightSum = Vector128.ConditionalSelect(isPositive, weightSum + weight, weightSum);
                sumX = Vector128.ConditionalSelect(isPositive, sumX + (weight * neighborX), sumX);
                sumY = Vector128.ConditionalSelect(isPositive, sumY + (weight * neighborY), sumY);
                sumB = Vector128.ConditionalSelect(isPositive, sumB + (weight * neighborB), sumB);
            }

            var inverseWeight = Vector128<float>.One / weightSum;
            unsafe
            {
                (sumX * inverseWeight).StoreUnsafe(ref targetX, (nuint)x);
                (sumY * inverseWeight).StoreUnsafe(ref targetY, (nuint)x);
                (sumB * inverseWeight).StoreUnsafe(ref targetB, (nuint)x);
            }
        }

        return x;
    }

    /// <summary>Gets the position of the first neighbor of the first sample of the row, for each neighbor, and returns the position of the sample.</summary>
    private static int GetNeighborOffsets(in EdgePreservingRow row, Span<int> neighborOffsets)
    {
        var border = (row.RowOffsets.Length - 1) / 2;
        var neighbors = row.Neighbors;
        for (var i = 0; i < neighbors.Length / 2; i++)
        {
            neighborOffsets[i] = row.RowOffsets[border + neighbors[i * 2]] + neighbors[(i * 2) + 1];
        }

        return row.RowOffsets[border];
    }

    /// <summary>
    /// The scalar reference of <see cref="FilterRow"/>, from the sample <paramref name="start"/> on: it reads the samples
    /// only, and computes each absolute difference where it uses it.
    /// </summary>
    internal static void FilterRowScalar(in EdgePreservingRow row, Span<float> outputX, Span<float> outputY, Span<float> outputB, int start = 0)
    {
        var samples = row.Samples;
        var channelLength = row.ChannelLength;
        var rowOffsets = row.RowOffsets;
        var border = (rowOffsets.Length - 1) / 2;
        var neighbors = row.Neighbors;
        var patch = row.Patch;
        var parameters = row.Parameters;
        for (var x = start; x < outputX.Length; x++)
        {
            var blockInverseSigma = row.SigmaRow[x >> 3];
            if (blockInverseSigma < MinInverseSigma)
                continue;

            var position = rowOffsets[border] + x;
            var scaledInverseSigma = blockInverseSigma * (row.IsBorderRow || (x & 7) is 0 or 7 ? parameters.BorderScale : parameters.SigmaScale);
            var weightSum = 1f;
            var sumX = samples[position];
            var sumY = samples[channelLength + position];
            var sumB = samples[(2 * channelLength) + position];
            for (var i = 0; i < neighbors.Length; i += 2)
            {
                var differenceX = 0f;
                var differenceY = 0f;
                var differenceB = 0f;
                for (var k = 0; k < patch.Length; k += 2)
                {
                    var reference = rowOffsets[border + patch[k]] + x + patch[k + 1];
                    var other = rowOffsets[border + patch[k] + neighbors[i]] + x + patch[k + 1] + neighbors[i + 1];
                    differenceX += MathF.Abs(samples[reference] - samples[other]);
                    differenceY += MathF.Abs(samples[channelLength + reference] - samples[channelLength + other]);
                    differenceB += MathF.Abs(samples[(2 * channelLength) + reference] - samples[(2 * channelLength) + other]);
                }

                var difference = (differenceX * parameters.ScaleX) + (differenceY * parameters.ScaleY) + (differenceB * parameters.ScaleB);
                var weight = (difference * scaledInverseSigma) + 1f;
                if (weight > 0)
                {
                    var neighbor = rowOffsets[border + neighbors[i]] + x + neighbors[i + 1];
                    weightSum += weight;
                    sumX += weight * samples[neighbor];
                    sumY += weight * samples[channelLength + neighbor];
                    sumB += weight * samples[(2 * channelLength) + neighbor];
                }
            }

            var inverseWeight = 1f / weightSum;
            outputX[x] = sumX * inverseWeight;
            outputY[x] = sumY * inverseWeight;
            outputB[x] = sumB * inverseWeight;
        }
    }

    /// <summary>The position inside a side of a position that may be outside: the side is mirrored at its two ends, which are repeated.</summary>
    internal static int Mirror(int position, int size)
    {
        while (position < 0 || position >= size)
        {
            position = position < 0 ? -position - 1 : (2 * size) - 1 - position;
        }

        return position;
    }

    private static int Modulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;

    /// <summary>Fills the border of a row of a ring with the mirror image of its samples.</summary>
    private static void FillBorder(Span<float> row, int border, int width)
    {
        for (var x = 1; x <= border; x++)
        {
            row[border - x] = row[border + Mirror(-x, width)];
            row[border + width - 1 + x] = row[border + Mirror(width - 1 + x, width)];
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

    /// <summary>A row that a step of the edge-preserving filter filters: the rows it reads, the distances of its samples to their neighbors and its parameters.</summary>
    /// <param name="samples">The rows of the three channels, one channel after the other.</param>
    /// <param name="channelLength">The distance between two channels in <paramref name="samples"/>.</param>
    /// <param name="rowOffsets">The position of the first sample of each row from the reach of the step above the row to its reach below.</param>
    /// <param name="neighbors">The neighbors averaged with a sample, as (row, column) offsets.</param>
    /// <param name="patch">The samples over which a neighbor is compared with the sample, as (row, column) offsets.</param>
    /// <param name="distances">
    /// The distance of the samples to their neighbors in each direction of the step, one channel after the other: the sum
    /// over the patch of the absolute differences of the samples with their neighbor, from zero, in the order of the patch.
    /// </param>
    /// <param name="distanceChannelLength">The distance between two channels in <paramref name="distances"/>.</param>
    /// <param name="distanceOffsets">For each neighbor, the position in <paramref name="distances"/> of the distance of the first sample of the row to it.</param>
    /// <param name="sigmaRow">The inverse strength of each block of the row.</param>
    /// <param name="isBorderRow">Whether the row is the first or the last of its blocks.</param>
    /// <param name="parameters">The scales of the step.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly ref struct EdgePreservingRow(
        ReadOnlySpan<float> samples,
        int channelLength,
        ReadOnlySpan<int> rowOffsets,
        ReadOnlySpan<sbyte> neighbors,
        ReadOnlySpan<sbyte> patch,
        ReadOnlySpan<float> distances,
        int distanceChannelLength,
        ReadOnlySpan<int> distanceOffsets,
        ReadOnlySpan<float> sigmaRow,
        bool isBorderRow,
        EdgePreservingStep parameters)
    {
        public ReadOnlySpan<float> Samples { get; } = samples;

        public int ChannelLength { get; } = channelLength;

        public ReadOnlySpan<int> RowOffsets { get; } = rowOffsets;

        public ReadOnlySpan<sbyte> Neighbors { get; } = neighbors;

        public ReadOnlySpan<sbyte> Patch { get; } = patch;

        public ReadOnlySpan<float> Distances { get; } = distances;

        public int DistanceChannelLength { get; } = distanceChannelLength;

        public ReadOnlySpan<int> DistanceOffsets { get; } = distanceOffsets;

        public ReadOnlySpan<float> SigmaRow { get; } = sigmaRow;

        public bool IsBorderRow { get; } = isBorderRow;

        public EdgePreservingStep Parameters { get; } = parameters;
    }

    /// <summary>
    /// A filter of the pipeline and its working memory: a ring of the rows it reads, the three channels one after the
    /// other, then, for a step of the edge-preserving filter, for each direction of the step and each channel, a ring of
    /// the absolute differences of these rows with their neighbor in that direction, and a ring of their sums over the
    /// patch: the distance of a sample to its neighbor in that direction.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Stage
    {
        public Stage(int step, int width, long start, int outputStart, int outputEnd)
        {
            Step = step;
            Border = GetBorder(step);
            NextOutput = outputStart;
            OutputEnd = outputEnd;
            Rows = (2 * Border) + 1;
            PaddedWidth = width + (2 * Border);
            Start = checked((int)start);
            ChannelLength = checked(Rows * PaddedWidth);
            var length = 3L * ChannelLength;
            if (step != GaborishStep)
            {
                var patch = GetPatch(step);
                var directions = GetDirections(step);
                int firstRow = 0, lastRow = 0, deepestDirection = 0;
                for (var k = 0; k < patch.Length; k += 2)
                {
                    firstRow = Math.Min(firstRow, patch[k]);
                    lastRow = Math.Max(lastRow, patch[k]);
                }

                for (var d = 0; d < directions.Length; d += 2)
                {
                    deepestDirection = Math.Max(deepestDirection, directions[d]);
                }

                // The distance to a neighbor in the opposite direction is the distance of that neighbor, on a row up to
                // the deepest direction above; the distances of a row sum the differences of the rows of the patch
                DistanceRows = deepestDirection + 1;
                NextDistance = outputStart - deepestDirection;
                DifferenceRows = lastRow - firstRow + 1;
                NextDifference = NextDistance + firstRow;
                DifferenceStart = checked((int)(start + length));
                DifferenceChannelLength = checked(DifferenceRows * PaddedWidth);
                DirectionLength = checked(3 * DifferenceChannelLength);
                length += (long)DirectionLength * (directions.Length / 2);
                DistanceStart = checked((int)(start + length));
                DistanceChannelLength = checked(DistanceRows * PaddedWidth);
                DistanceDirectionLength = checked(3 * DistanceChannelLength);
                length += (long)DistanceDirectionLength * (directions.Length / 2);
            }

            Length = length;
        }

        /// <summary>Gets the filter: <see cref="GaborishStep"/> or the step of the edge-preserving filter.</summary>
        public int Step { get; }

        /// <summary>Gets the reach of the filter: the number of rows it reads above and below a row, and of samples on each side.</summary>
        public int Border { get; }

        /// <summary>Gets the number of rows of the ring.</summary>
        public int Rows { get; }

        /// <summary>Gets the length of a row of the ring, borders included.</summary>
        public int PaddedWidth { get; }

        /// <summary>Gets the position of the ring in the working memory.</summary>
        public int Start { get; }

        /// <summary>Gets the distance between two channels of the ring.</summary>
        public int ChannelLength { get; }

        /// <summary>Gets the number of rows of differences in each direction and channel.</summary>
        public int DifferenceRows { get; }

        /// <summary>Gets the position of the differences in the working memory.</summary>
        public int DifferenceStart { get; }

        /// <summary>Gets the distance between two channels of the differences of a direction.</summary>
        public int DifferenceChannelLength { get; }

        /// <summary>Gets the distance between the differences of two directions.</summary>
        public int DirectionLength { get; }

        /// <summary>Gets the number of rows of distances in each direction and channel.</summary>
        public int DistanceRows { get; }

        /// <summary>Gets the position of the distances in the working memory.</summary>
        public int DistanceStart { get; }

        /// <summary>Gets the distance between two channels of the distances of a direction.</summary>
        public int DistanceChannelLength { get; }

        /// <summary>Gets the distance between the distances of two directions.</summary>
        public int DistanceDirectionLength { get; }

        /// <summary>Gets the working memory of the filter.</summary>
        public long Length { get; }

        /// <summary>Gets the row after the last row the filter produces.</summary>
        public int OutputEnd { get; }

        /// <summary>Gets or sets the number of rows the filter has received.</summary>
        public int Received { get; set; }

        /// <summary>Gets or sets the next row the filter produces.</summary>
        public int NextOutput { get; set; }

        /// <summary>Gets or sets the next row whose differences are computed; rows above the frame or the band are mirrored rows or rows the filter does not produce.</summary>
        public int NextDifference { get; set; }

        /// <summary>Gets or sets the next row whose distances are computed.</summary>
        public int NextDistance { get; set; }
    }

    /// <summary>The filters of a frame, in the order they apply, and the rows that go through them.</summary>
    private readonly ref struct Pipeline
    {
        private readonly Span<float> _planeX;
        private readonly Span<float> _planeY;
        private readonly Span<float> _planeB;
        private readonly int _stride;
        private readonly int _width;
        private readonly int _height;
        private readonly Span<float> _buffer;
        private readonly Span<Stage> _stages;
        private readonly JpegXlRestorationFilter _filter;
        private readonly ReadOnlySpan<float> _inverseSigma;
        private readonly int _blocksWide;
        private readonly int _start;
        private readonly int _end;
        private readonly int _first;
        private readonly ReadOnlySpan<float> _halo;
        private readonly int _haloRows;

        public Pipeline(Span<float> planeX, Span<float> planeY, Span<float> planeB, int stride, int width, int height, Span<float> buffer, Span<Stage> stages, JpegXlRestorationFilter filter, ReadOnlySpan<float> inverseSigma, int blocksWide, int start, int end, int first, ReadOnlySpan<float> halo)
        {
            _start = start;
            _end = end;
            _first = first;
            _halo = halo;
            _haloRows = width == 0 ? 0 : halo.Length / (3 * width);
            _planeX = planeX;
            _planeY = planeY;
            _planeB = planeB;
            _stride = stride;
            _width = width;
            _height = height;
            _buffer = buffer;
            _stages = stages;
            _filter = filter;
            _inverseSigma = inverseSigma;
            _blocksWide = blocksWide;
        }

        /// <summary>
        /// Gives a row of the channels to the first filter, then lets every filter produce the rows it can. A row outside
        /// the band is read from the copy of the rows around it, which the neighbor bands may have filtered already.
        /// </summary>
        public void Receive(int y)
        {
            ref var first = ref _stages[0];
            for (var c = 0; c < 3; c++)
            {
                var target = GetRingRow(first, c, y % first.Rows);
                ReadOnlySpan<float> source;
                if (y >= _start && y < _end)
                {
                    source = GetPlane(c).Slice(y * _stride, _width);
                }
                else
                {
                    var index = y < _start ? y - _first : _start - _first + (y - _end);
                    source = _halo.Slice(((c * _haloRows) + index) * _width, _width);
                }

                source.CopyTo(target[first.Border..]);
                FillBorder(target, first.Border, _width);
            }

            Deliver(0, y);
        }

        private Span<float> GetPlane(int channel) => channel switch
        {
            0 => _planeX,
            1 => _planeY,
            _ => _planeB,
        };

        private Span<float> GetRingRow(in Stage stage, int channel, int slot) => _buffer.Slice(stage.Start + (channel * stage.ChannelLength) + (slot * stage.PaddedWidth), stage.PaddedWidth);

        /// <summary>Records that a filter has received a row, and produces the rows it can, each given to the next filter.</summary>
        private void Deliver(int index, int row)
        {
            ref var stage = ref _stages[index];
            stage.Received = row + 1;
            while (stage.NextOutput < stage.OutputEnd && stage.Received >= Math.Min(stage.NextOutput + stage.Border + 1, _height))
            {
                var y = stage.NextOutput;
                stage.NextOutput = y + 1;
                if (stage.Step == GaborishStep)
                {
                    ProduceGaborish(stage, index, y);
                }
                else
                {
                    ProduceEdgePreserving(ref stage, index, y);
                }

                if (index + 1 < _stages.Length)
                {
                    ref var next = ref _stages[index + 1];
                    for (var c = 0; c < 3; c++)
                    {
                        FillBorder(GetRingRow(next, c, y % next.Rows), next.Border, _width);
                    }

                    Deliver(index + 1, y);
                }
            }
        }

        /// <summary>Gets where a filter writes a row: the ring of the next filter, or the channel for the last one.</summary>
        private Span<float> GetTarget(int index, int channel, int y)
        {
            if (index + 1 == _stages.Length)
                return GetPlane(channel).Slice(y * _stride, _width);

            ref var next = ref _stages[index + 1];
            return GetRingRow(next, channel, y % next.Rows).Slice(next.Border, _width);
        }

        private void ProduceGaborish(in Stage stage, int index, int y)
        {
            var top = Mirror(y - 1, _height) % stage.Rows;
            var middle = y % stage.Rows;
            var bottom = Mirror(y + 1, _height) % stage.Rows;
            for (var c = 0; c < 3; c++)
            {
                // The weights of the center, of the four sides and of the four corners, normalized
                var side = _filter.GaborWeights[c * 2];
                var corner = _filter.GaborWeights[(c * 2) + 1];
                var scale = 1f / (1f + (4f * (side + corner)));
                GaborishRow(GetRingRow(stage, c, top), GetRingRow(stage, c, middle), GetRingRow(stage, c, bottom), scale, side * scale, corner * scale, GetTarget(index, c, y));
            }
        }

        private void ProduceEdgePreserving(ref Stage stage, int index, int y)
        {
            var step = stage.Step;
            var neighbors = GetNeighbors(step);
            var patch = GetPatch(step);
            var directions = GetDirections(step);
            var border = stage.Border;
            var paddedWidth = stage.PaddedWidth;

            // The rows the step reads, mirrored at the top and bottom of the frame
            Span<int> rowOffsets = stackalloc int[(2 * MaxBorder) + 1];
            rowOffsets = rowOffsets[..((2 * border) + 1)];
            for (var dy = -border; dy <= border; dy++)
            {
                rowOffsets[dy + border] = (Mirror(y + dy, _height) % stage.Rows * paddedWidth) + border;
            }

            // The distances of the row, from the differences of the rows of its patch
            var lastPatchRow = 0;
            for (var k = 0; k < patch.Length; k += 2)
            {
                lastPatchRow = Math.Max(lastPatchRow, patch[k]);
            }

            while (stage.NextDistance <= y)
            {
                while (stage.NextDifference <= stage.NextDistance + lastPatchRow)
                {
                    ComputeDifferences(stage, stage.NextDifference, patch, directions);
                    stage.NextDifference++;
                }

                ComputeDistances(stage, stage.NextDistance, patch, directions);
                stage.NextDistance++;
            }

            // The distance to a neighbor is the distance in its direction, or, for the opposite direction, the distance of
            // the neighbor in that direction: the absolute difference of two samples does not depend on their order
            Span<int> distanceOffsets = stackalloc int[MaxNeighbors];
            distanceOffsets = distanceOffsets[..(neighbors.Length / 2)];
            for (var i = 0; i < distanceOffsets.Length; i++)
            {
                var direction = FindDirection(directions, neighbors[i * 2], neighbors[(i * 2) + 1], out var isOpposite);
                int rowShift = 0, columnShift = 0;
                if (isOpposite)
                {
                    rowShift = -directions[direction * 2];
                    columnShift = -directions[(direction * 2) + 1];
                }

                distanceOffsets[i] = (direction * stage.DistanceDirectionLength) + (Modulo(y + rowShift, stage.DistanceRows) * paddedWidth) + border + columnShift;
            }

            // A block that the filter leaves keeps its samples: they are copied first
            var samples = _buffer.Slice(stage.Start, 3 * stage.ChannelLength);
            var outputX = GetTarget(index, 0, y);
            var outputY = GetTarget(index, 1, y);
            var outputB = GetTarget(index, 2, y);
            samples.Slice(rowOffsets[border], _width).CopyTo(outputX);
            samples.Slice(stage.ChannelLength + rowOffsets[border], _width).CopyTo(outputY);
            samples.Slice((2 * stage.ChannelLength) + rowOffsets[border], _width).CopyTo(outputB);

            // The differences count less on the border of a block of 8x8 samples, where the transforms leave discontinuities
            var sigmaScale = step switch
            {
                0 => _filter.EpfPass0SigmaScale * 1.65f,
                1 => 1.65f,
                _ => _filter.EpfPass2SigmaScale * 1.65f,
            };
            var parameters = new EdgePreservingStep(sigmaScale, sigmaScale * _filter.EpfBorderSadMultiplier, _filter.EpfChannelScale[0], _filter.EpfChannelScale[1], _filter.EpfChannelScale[2]);
            var distances = _buffer.Slice(stage.DistanceStart, directions.Length / 2 * stage.DistanceDirectionLength);
            var sigmaRow = _inverseSigma.Slice((y >> 3) * _blocksWide, _blocksWide);
            var row = new EdgePreservingRow(samples, stage.ChannelLength, rowOffsets, neighbors, patch, distances, stage.DistanceChannelLength, distanceOffsets, sigmaRow, (y & 7) is 0 or 7, parameters);
            FilterRow(row, outputX, outputY, outputB);
        }

        /// <summary>Gets the columns of the distances a step reads in a direction: from the first sample of a row and from its last sample, both shifted by the opposite direction.</summary>
        private (int First, int Last) GetDistanceColumns(int columnStep) => (Math.Min(0, -columnStep), _width - 1 + Math.Max(0, -columnStep));

        /// <summary>
        /// Computes the absolute differences of a row, which may be above or below the frame, with its neighbor in each
        /// direction, over the columns the distances read them at.
        /// </summary>
        private void ComputeDifferences(in Stage stage, int y, ReadOnlySpan<sbyte> patch, ReadOnlySpan<sbyte> directions)
        {
            int lowestColumn = 0, highestColumn = 0;
            for (var k = 1; k < patch.Length; k += 2)
            {
                lowestColumn = Math.Min(lowestColumn, patch[k]);
                highestColumn = Math.Max(highestColumn, patch[k]);
            }

            var border = stage.Border;
            var paddedWidth = stage.PaddedWidth;
            var differenceRow = Modulo(y, stage.DifferenceRows) * paddedWidth;
            for (var d = 0; d < directions.Length / 2; d++)
            {
                var rowStep = directions[d * 2];
                var columnStep = directions[(d * 2) + 1];
                var (first, last) = GetDistanceColumns(columnStep);
                first += lowestColumn;
                last += highestColumn;
                var count = last - first + 1;
                var sample = (Mirror(y, _height) % stage.Rows * paddedWidth) + border + first;
                var neighbor = (Mirror(y + rowStep, _height) % stage.Rows * paddedWidth) + border + first + columnStep;
                for (var c = 0; c < 3; c++)
                {
                    var ring = _buffer.Slice(stage.Start + (c * stage.ChannelLength), stage.ChannelLength);
                    var target = _buffer.Slice(stage.DifferenceStart + (d * stage.DirectionLength) + (c * stage.DifferenceChannelLength) + differenceRow + border + first, count);
                    AbsoluteDifferences(ring.Slice(sample, count), ring.Slice(neighbor, count), target);
                }
            }
        }

        /// <summary>Computes the distances of a row, which may be above the frame, to its neighbor in each direction: the sums of the differences over the patch.</summary>
        private void ComputeDistances(in Stage stage, int y, ReadOnlySpan<sbyte> patch, ReadOnlySpan<sbyte> directions)
        {
            var border = stage.Border;
            var paddedWidth = stage.PaddedWidth;
            Span<int> terms = stackalloc int[MaxPatchLength];
            terms = terms[..(patch.Length / 2)];
            for (var k = 0; k < terms.Length; k++)
            {
                terms[k] = (Modulo(y + patch[k * 2], stage.DifferenceRows) * paddedWidth) + border + patch[(k * 2) + 1];
            }

            var distanceRow = (Modulo(y, stage.DistanceRows) * paddedWidth) + border;
            for (var d = 0; d < directions.Length / 2; d++)
            {
                var (first, last) = GetDistanceColumns(directions[(d * 2) + 1]);
                for (var c = 0; c < 3; c++)
                {
                    var differences = _buffer.Slice(stage.DifferenceStart + (d * stage.DirectionLength) + (c * stage.DifferenceChannelLength), stage.DifferenceChannelLength);
                    var target = _buffer.Slice(stage.DistanceStart + (d * stage.DistanceDirectionLength) + (c * stage.DistanceChannelLength) + distanceRow + first, last - first + 1);
                    SumPatch(differences, terms, first, target);
                }
            }
        }

        /// <summary>Finds the direction of a neighbor, or the opposite one.</summary>
        private static int FindDirection(ReadOnlySpan<sbyte> directions, int rowOffset, int columnOffset, out bool isOpposite)
        {
            for (var d = 0; d < directions.Length / 2; d++)
            {
                if (directions[d * 2] == rowOffset && directions[(d * 2) + 1] == columnOffset)
                {
                    isOpposite = false;
                    return d;
                }

                if (directions[d * 2] == -rowOffset && directions[(d * 2) + 1] == -columnOffset)
                {
                    isOpposite = true;
                    return d;
                }
            }

            throw new InvalidOperationException("A neighbor of the edge-preserving filter has no direction.");
        }
    }
}
