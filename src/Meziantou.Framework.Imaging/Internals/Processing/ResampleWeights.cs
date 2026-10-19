using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The normalized, edge-clamped filter weights of one resize axis, stored
/// in buffers charged to the allocation scope of the image.
/// </summary>
/// <remarks>
/// <para>For output pixel <c>d</c> with center <c>c = u(d) - 0.5</c> (source index coordinates) and filter scale
/// <c>f = max(1, 1 / s)</c>, the support is <c>r * f</c> (<c>r</c> is the kernel radius). The contributors are the
/// integers <c>i</c> with <c>|i - c| &lt; r * f</c>: <c>lo = floor(c - r f) + 1</c> to <c>hi = ceil(c + r f) - 1</c>. Their raw
/// weights are <c>K((i - c) / f)</c>, normalized by their sum (accumulated in increasing <c>i</c>). Contributors outside the
/// source are clamped to the nearest edge pixel: their normalized weights are added, in increasing <c>i</c>, to the weight
/// of pixel 0 or <c>n - 1</c>. The window of <c>d</c> is therefore <c>[clamp(lo), clamp(hi)]</c>; windows are
/// non-decreasing in <c>d</c>, which the streaming vertical pass relies on.</para>
/// <para>
/// The weights of the horizontal pass of a resize are padded (<see cref="CreatePadded"/>): every window is widened with zero
/// weights to the same number of taps (<see cref="PaddedCount"/>), so that the vector loops sum several output pixels with
/// one loop and no tails. The padding taps come before the window only where the window would otherwise end after
/// <see cref="Last"/>. A padding tap adds <c>0 x</c> to the sum of the scalar loop: the working samples are finite (they
/// are computed from 8- or 16-bit samples), so that product is a zero, and a sum started from <c>+0</c> is never <c>-0</c>
/// (in round-to-nearest, <c>a + b</c> is <c>-0</c> only when both are), so adding a zero, before or after the other taps,
/// leaves every bit of it unchanged: <c>+0 + -0</c> is <c>+0</c> and <c>s + 0</c> is <c>s</c> for a nonzero <c>s</c>.
/// </para>
/// </remarks>
internal sealed class ResampleWeights : IDisposable
{
    private readonly PooledBuffer _windows;
    private readonly PooledBuffer _weights;

    private ResampleWeights(int outputLength, int first, int last, int paddedCount, int group, PooledBuffer windows, PooledBuffer weights)
    {
        OutputLength = outputLength;
        First = first;
        Last = last;
        PaddedCount = paddedCount;
        Group = group;
        _windows = windows;
        _weights = weights;
    }

    /// <summary>Gets the number of output pixels.</summary>
    public int OutputLength { get; }

    /// <summary>Gets the first source index used by any output pixel.</summary>
    public int First { get; }

    /// <summary>Gets the last source index used by any output pixel.</summary>
    public int Last { get; }

    /// <summary>
    /// Gets the number of taps of every padded window (<see cref="CreatePadded"/>: a multiple of <see cref="Group"/>, at least
    /// <see cref="MaxCount"/>), or 0 when the windows are not padded.
    /// </summary>
    public int PaddedCount { get; }

    /// <summary>
    /// Gets the number of output pixels whose padded weights are interleaved: tap <c>k</c> of output pixel <c>d</c> is at
    /// <c>((d / Group) * PaddedCount + k) * Group + d % Group</c> (1: the padded weights of an output pixel are contiguous).
    /// </summary>
    public int Group { get; }

    /// <summary>
    /// Gets the number of source indexes that the padded windows read from <see cref="First"/>: <c>Last - First + 1</c>, or
    /// <see cref="PaddedCount"/> when that is larger.
    /// </summary>
    public int PaddedSourceLength => Math.Max(Last - First + 1, PaddedCount);

    /// <summary>Gets the largest window.</summary>
    public int MaxCount { get; private set; }

    /// <summary>
    /// Gets the largest number of output pixels whose windows contain a common source index (the number of accumulators a
    /// scatter pass needs).
    /// </summary>
    public int MaxOverlap { get; private set; }

    private Span<int> Windows => unsafe(MemoryMarshal.Cast<byte, int>(_windows.Span));

    /// <summary>Computes the weights of an axis.</summary>
    /// <exception cref="ImageResourceLimitException">The tables exceed the allocation limit; nothing stays charged.</exception>
    public static ResampleWeights Create(AllocationScope scope, ResampleAxis axis, ResamplingFilter filter) => Create(scope, axis, filter, group: 0);

    /// <summary>
    /// Computes the weights of an axis, every window padded with zero weights to <see cref="PaddedCount"/> taps and the weights
    /// of <paramref name="group"/> consecutive output pixels interleaved (see <see cref="Group"/>).
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The tables exceed the allocation limit; nothing stays charged.</exception>
    public static ResampleWeights CreatePadded(AllocationScope scope, ResampleAxis axis, ResamplingFilter filter, int group)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(group);
        return Create(scope, axis, filter, group);
    }

    // Group 0: every window has its own length
    private static ResampleWeights Create(AllocationScope scope, ResampleAxis axis, ResamplingFilter filter, int group)
    {
        var outputLength = axis.OutputLength;
        var sourceLength = axis.SourceLength;
        var scale = axis.FilterScale;
        var support = ResamplingKernels.GetRadius(filter) * scale;

        // First pass: the windows (start, count, then the offset of the weights, or the start of the padded window) and the
        // total number of weights
        var windows = Rent<int>(scope, 3L * outputLength);
        PooledBuffer? weightBuffer = null;
        try
        {
            var table = unsafe(MemoryMarshal.Cast<byte, int>(windows.Span));
            long total = 0;
            var maxCount = 0;
            for (var d = 0; d < outputLength; d++)
            {
                var (lo, hi) = GetContributors(axis.GetCenter(d), support);
                var start = Clamp(lo, sourceLength);
                var count = Clamp(hi, sourceLength) - start + 1;
                table[3 * d] = start;
                table[(3 * d) + 1] = count;
                table[(3 * d) + 2] = (int)total;
                total += count;
                maxCount = Math.Max(maxCount, count);
                if (total > CheckedSizes.MaxBufferLength / sizeof(double))
                    throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, scope.Limit, total * sizeof(double));
            }

            var first = table[0];
            var last = table[3 * (outputLength - 1)] + table[(3 * (outputLength - 1)) + 1] - 1;
            var paddedCount = 0;
            if (group is not 0)
            {
                // A padded window starts with its window, or earlier when it would otherwise end after the last source index
                // (but never before the first one): the padded windows contain their windows and stay within
                // [first, first + PaddedSourceLength - 1]
                paddedCount = (maxCount + group - 1) / group * group;
                total = ((long)outputLength + group - 1) / group * group * paddedCount;
                if (total > CheckedSizes.MaxBufferLength / sizeof(double))
                    throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, scope.Limit, total * sizeof(double));

                for (var d = 0; d < outputLength; d++)
                {
                    table[(3 * d) + 2] = Math.Max(first, Math.Min(table[3 * d], last + 1 - paddedCount));
                }
            }

            weightBuffer = Rent<double>(scope, total);
            var result = new ResampleWeights(outputLength, first, last, paddedCount, Math.Max(group, 1), windows, weightBuffer);
            result.Fill(axis, filter, scale, support);
            return result;
        }
        catch
        {
            weightBuffer?.Dispose();
            windows.Dispose();
            throw;
        }
    }

    /// <summary>Gets the first source index of the window of output pixel <paramref name="index"/>.</summary>
    public int GetStart(int index) => Windows[3 * index];

    /// <summary>Gets the length of the window of output pixel <paramref name="index"/>.</summary>
    public int GetCount(int index) => Windows[(3 * index) + 1];

    /// <summary>Gets the weights of the window of output pixel <paramref name="index"/>, in increasing source index.</summary>
    /// <exception cref="InvalidOperationException">The padded weights of several output pixels are interleaved.</exception>
    public ReadOnlySpan<double> GetWeights(int index)
    {
        if (Group is not 1)
            throw new InvalidOperationException("The weights of an output pixel are interleaved with those of other output pixels.");

        var windows = Windows;
        var offset = PaddedCount is 0 ? windows[(3 * index) + 2] : (index * PaddedCount) + windows[3 * index] - windows[(3 * index) + 2];
        return unsafe(MemoryMarshal.Cast<byte, double>(_weights.Span)).Slice(offset, windows[(3 * index) + 1]);
    }

    /// <summary>Gets the padded windows and weights as spans, for the loops that read the window of every output pixel.</summary>
    public WindowTable GetWindowTable()
    {
        Debug.Assert(PaddedCount is not 0);
        return new(Windows, unsafe(MemoryMarshal.Cast<byte, double>(_weights.Span)));
    }

    public void Dispose()
    {
        _weights.Dispose();
        _windows.Dispose();
    }

    /// <summary>Rents a zeroed buffer of <paramref name="count"/> elements from the scope (temporary operation storage).</summary>
    /// <exception cref="ImageResourceLimitException">The buffer exceeds the allocation limit or the maximum buffer length.</exception>
    internal static PooledBuffer Rent<T>(AllocationScope scope, long count)
        where T : unmanaged
    {
        if (!CheckedSizes.TryMultiply(Math.Max(count, 1), Unsafe.SizeOf<T>(), out var bytes) || bytes > CheckedSizes.MaxBufferLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, scope.Limit, bytes);

        return scope.Rent((int)bytes, AllocationKind.Temporary, clear: true);
    }

    /// <summary>Gets the contributors <c>i</c> with <c>|i - center| &lt; support</c>.</summary>
    private static (long Low, long High) GetContributors(double center, double support)
        => ((long)Math.Floor(center - support) + 1, (long)Math.Ceiling(center + support) - 1);

    private static int Clamp(long index, int length) => index < 0 ? 0 : index >= length ? length - 1 : (int)index;

    private void Fill(ResampleAxis axis, ResamplingFilter filter, double scale, double support)
    {
        var sourceLength = axis.SourceLength;
        var all = unsafe(MemoryMarshal.Cast<byte, double>(_weights.Span));
        var windows = Windows;
        var maxCount = 0;
        var maxOverlap = 0;
        var oldest = 0;
        for (var d = 0; d < OutputLength; d++)
        {
            var center = axis.GetCenter(d);
            var (lo, hi) = GetContributors(center, support);
            var sum = 0.0;
            for (var i = lo; i <= hi; i++)
            {
                sum += Evaluate(filter, i, center, scale);
            }

            Debug.Assert(sum > 0);
            var start = windows[3 * d];
            var count = windows[(3 * d) + 1];
            if (PaddedCount is 0)
            {
                var weights = all.Slice(windows[(3 * d) + 2], count);
                for (var i = lo; i <= hi; i++)
                {
                    weights[Clamp(i, sourceLength) - start] += Evaluate(filter, i, center, scale) / sum;
                }
            }
            else
            {
                // The same weights at their taps of the padded window (see Group)
                var paddedStart = windows[(3 * d) + 2];
                var weights = all.Slice(d / Group * PaddedCount * Group, PaddedCount * Group);
                var lane = d % Group;
                for (var i = lo; i <= hi; i++)
                {
                    weights[((Clamp(i, sourceLength) - paddedStart) * Group) + lane] += Evaluate(filter, i, center, scale) / sum;
                }
            }

            maxCount = Math.Max(maxCount, count);

            // Outputs still accumulating when d starts: those whose window ends at or after the start of d
            while (windows[(3 * oldest) + 0] + windows[(3 * oldest) + 1] - 1 < start)
            {
                oldest++;
            }

            maxOverlap = Math.Max(maxOverlap, d - oldest + 1);
        }

        MaxCount = maxCount;
        MaxOverlap = maxOverlap;
    }

    private static double Evaluate(ResamplingFilter filter, long index, double center, double scale)
    {
        var distance = index - center;
        return ResamplingKernels.Evaluate(filter, scale == 1 ? distance : distance / scale);
    }

    /// <summary>The padded windows and weights of an axis as spans, without the per-call casts.</summary>
    internal readonly ref struct WindowTable
    {
        private readonly ReadOnlySpan<int> _windows;

        public WindowTable(ReadOnlySpan<int> windows, ReadOnlySpan<double> weights)
        {
            _windows = windows;
            Weights = weights;
        }

        /// <summary>Gets the padded weights (see <see cref="Group"/>).</summary>
        public ReadOnlySpan<double> Weights { get; }

        /// <summary>Gets the first source index of the padded window of output pixel <paramref name="index"/>.</summary>
        public int GetPaddedStart(int index) => _windows[(3 * index) + 2];
    }
}
