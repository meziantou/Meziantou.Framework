using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A rectangle of the canvas that a filter works on: four single-precision values per pixel, red, green, blue and alpha,
/// the colors multiplied by the alpha, as fractions of one. Rented from an allocation scope.
/// </summary>
internal sealed class FilterSurface : IDisposable
{
    private PooledBuffer? _buffer;

    /// <exception cref="ImageResourceLimitException">The surface exceeds the allocation limit.</exception>
    public FilterSurface(AllocationScope scope, Rectangle bounds)
    {
        Bounds = bounds;
        _buffer = ResampleWeights.Rent<float>(scope, 4L * Math.Max(bounds.Width, 1) * Math.Max(bounds.Height, 1));
    }

    /// <summary>Gets the pixels of the canvas the surface holds.</summary>
    public Rectangle Bounds { get; }

    /// <summary>Gets every value, row by row.</summary>
    public Span<float> Values => unsafe(MemoryMarshal.Cast<byte, float>(_buffer!.Span))[..(4 * Bounds.Width * Bounds.Height)];

    /// <summary>Gets the values of a row of the canvas, from the left of the surface.</summary>
    public Span<float> GetRow(int y) => Values.Slice(4 * (y - Bounds.Top) * Bounds.Width, 4 * Bounds.Width);

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
