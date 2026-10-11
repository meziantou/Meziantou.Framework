namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The samples of one plane of an AV1 frame: rows of 16-bit samples for every bit depth, in one buffer rented from the decoder's
/// allocation scope. The plane is allocated for whole superblocks, so that a block that crosses the edge of the frame is
/// predicted and reconstructed without bounds handling; only <see cref="Width"/> x <see cref="Height"/> samples are part of the
/// frame.
/// </summary>
internal sealed class Av1Plane : IDisposable
{
    private readonly Av1PooledArray<ushort> _samples;

    private Av1Plane(Av1PooledArray<ushort> samples, int width, int height, int stride, int allocatedHeight)
    {
        _samples = samples;
        Width = width;
        Height = height;
        Stride = stride;
        AllocatedHeight = allocatedHeight;
    }

    /// <summary>Gets the number of samples of a row that are part of the frame.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows that are part of the frame.</summary>
    public int Height { get; }

    /// <summary>Gets the number of samples of a row of <see cref="Samples"/>.</summary>
    public int Stride { get; }

    /// <summary>Gets the number of rows of <see cref="Samples"/>.</summary>
    public int AllocatedHeight { get; }

    /// <summary>Gets the samples: <see cref="AllocatedHeight"/> rows of <see cref="Stride"/> samples.</summary>
    public Span<ushort> Samples => _samples.Span;

    /// <summary>Gets the row <paramref name="y"/> of the frame: <see cref="Width"/> samples.</summary>
    public Span<ushort> GetRow(int y) => _samples.Span.Slice(y * Stride, Width);

    /// <summary>
    /// Rents a zeroed plane of <paramref name="width"/> x <paramref name="height"/> samples, allocated for a multiple of
    /// <paramref name="alignment"/> samples in both dimensions.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The plane exceeds the live-allocation limit or the maximum buffer length.</exception>
    public static Av1Plane Create(AllocationScope scope, int width, int height, int alignment)
    {
        var stride = ((long)width + alignment - 1) / alignment * alignment;
        var allocatedHeight = ((long)height + alignment - 1) / alignment * alignment;
        var samples = Av1PooledArray<ushort>.Rent(scope, stride * allocatedHeight);
        return new Av1Plane(samples, width, height, (int)stride, (int)allocatedHeight);
    }

    public void Dispose() => _samples.Dispose();
}
