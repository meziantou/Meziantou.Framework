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
/// </remarks>
internal sealed class ResampleWeights : IDisposable
{
    private readonly PooledBuffer _windows;
    private readonly PooledBuffer _weights;

    private ResampleWeights(int outputLength, PooledBuffer windows, PooledBuffer weights)
    {
        OutputLength = outputLength;
        _windows = windows;
        _weights = weights;
    }

    /// <summary>Gets the number of output pixels.</summary>
    public int OutputLength { get; }

    /// <summary>Gets the first source index used by any output pixel.</summary>
    public int First => GetStart(0);

    /// <summary>Gets the last source index used by any output pixel.</summary>
    public int Last => GetStart(OutputLength - 1) + GetCount(OutputLength - 1) - 1;

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
    public static ResampleWeights Create(AllocationScope scope, ResampleAxis axis, ResamplingFilter filter)
    {
        var outputLength = axis.OutputLength;
        var sourceLength = axis.SourceLength;
        var scale = axis.FilterScale;
        var support = ResamplingKernels.GetRadius(filter) * scale;

        // First pass: the windows (start, count, offset of the weights) and the total number of weights
        var windows = Rent<int>(scope, 3L * outputLength);
        PooledBuffer? weightBuffer = null;
        try
        {
            var table = unsafe(MemoryMarshal.Cast<byte, int>(windows.Span));
            long total = 0;
            for (var d = 0; d < outputLength; d++)
            {
                var (lo, hi) = GetContributors(axis.GetCenter(d), support);
                var start = Clamp(lo, sourceLength);
                var count = Clamp(hi, sourceLength) - start + 1;
                table[3 * d] = start;
                table[(3 * d) + 1] = count;
                table[(3 * d) + 2] = (int)total;
                total += count;
                if (total > CheckedSizes.MaxBufferLength / sizeof(double))
                    throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, scope.Limit, total * sizeof(double));
            }

            weightBuffer = Rent<double>(scope, total);
            var result = new ResampleWeights(outputLength, windows, weightBuffer);
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
    public ReadOnlySpan<double> GetWeights(int index)
    {
        var windows = Windows;
        return unsafe(MemoryMarshal.Cast<byte, double>(_weights.Span)).Slice(windows[(3 * index) + 2], windows[(3 * index) + 1]);
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
            var weights = all.Slice(windows[(3 * d) + 2], count);
            for (var i = lo; i <= hi; i++)
            {
                weights[Clamp(i, sourceLength) - start] += Evaluate(filter, i, center, scale) / sum;
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
}
