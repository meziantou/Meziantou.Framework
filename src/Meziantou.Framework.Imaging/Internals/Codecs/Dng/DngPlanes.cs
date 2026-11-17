using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An image of the DNG pipeline: <see cref="Planes"/> single-precision samples per pixel, interleaved, row by row, in one
/// buffer rented from the allocation scope of the operation (charged before it is rented).
/// </summary>
internal sealed class DngPlanes : IDisposable
{
    private PooledBuffer? _buffer;

    private DngPlanes(int width, int height, int planes, PooledBuffer buffer)
    {
        Width = width;
        Height = height;
        Planes = planes;
        _buffer = buffer;
    }

    public int Width { get; }

    public int Height { get; }

    public int Planes { get; }

    /// <summary>Gets the samples, pixel after pixel, row after row.</summary>
    public Span<float> Samples => unsafe(MemoryMarshal.Cast<byte, float>(Buffer.Span[..(Width * Height * Planes * sizeof(float))]));

    private PooledBuffer Buffer => _buffer ?? throw new ObjectDisposedException(nameof(DngPlanes));

    /// <summary>Rents an image whose samples are cleared.</summary>
    /// <exception cref="ImageResourceLimitException">The image is larger than the limits allow.</exception>
    public static DngPlanes Create(ImageCodecContext context, int width, int height, int planes)
    {
        if (!CheckedSizes.TryMultiply((long)width * height, (long)planes * sizeof(float), out var length) || length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        var buffer = context.Scope.Rent((int)length, AllocationKind.DecoderState, clear: true);
        return new DngPlanes(width, height, planes, buffer);
    }

    /// <summary>Gets the samples of a row.</summary>
    public Span<float> GetRow(int y) => Samples.Slice(y * Width * Planes, Width * Planes);

    public float this[int x, int y, int plane]
    {
        get => Samples[(((y * Width) + x) * Planes) + plane];
        set => Samples[(((y * Width) + x) * Planes) + plane] = value;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
