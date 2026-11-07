using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A plane of 32-bit floats, row after row, rented from an allocation scope and filled with zeros
/// (<see cref="Ssimulacra2Calculator"/>, <see cref="ButteraugliCalculator"/>).
/// </summary>
internal sealed class FloatPlane : IDisposable
{
    private PooledBuffer? _buffer;

    /// <exception cref="ImageResourceLimitException">The plane exceeds the allocation limit.</exception>
    public FloatPlane(AllocationScope scope, int width, int height)
    {
        Width = width;
        Height = height;
        _buffer = ResampleWeights.Rent<float>(scope, (long)width * height);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the samples, <see cref="Width"/> per row.</summary>
    public Span<float> Span => unsafe(MemoryMarshal.Cast<byte, float>((_buffer ?? throw new ObjectDisposedException(nameof(FloatPlane))).Span))[..(Width * Height)];

    public Span<float> GetRow(int y) => Span.Slice(y * Width, Width);

    /// <summary>Gets the samples of the rows <c>[start, end)</c>.</summary>
    public Span<float> GetRows(int start, int end) => Span.Slice(start * Width, (end - start) * Width);

    /// <summary>Gets the size of the plane.</summary>
    public Size Size => new(Width, Height);

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
