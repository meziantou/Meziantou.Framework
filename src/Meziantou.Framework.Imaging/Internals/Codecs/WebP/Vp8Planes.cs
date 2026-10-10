namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The reconstructed Y'CbCr 4:2:0 planes of a VP8 frame, padded to whole macroblocks (16x16 luma, 8x8 chroma), rented from
/// the decoder's allocation scope (<see cref="AllocationKind.DecoderState"/>).
/// </summary>
internal sealed class Vp8Planes : IDisposable
{
    private PooledBuffer? _y;
    private PooledBuffer? _u;
    private PooledBuffer? _v;

    private Vp8Planes(int width, int height, PooledBuffer y, PooledBuffer u, PooledBuffer v)
    {
        Width = width;
        Height = height;
        YStride = ((width + 15) >> 4) * 16;
        UVStride = YStride / 2;
        _y = y;
        _u = u;
        _v = v;
    }

    /// <summary>Gets the visible width.</summary>
    public int Width { get; }

    /// <summary>Gets the visible height.</summary>
    public int Height { get; }

    /// <summary>Gets the luma row length (the width padded to a multiple of 16).</summary>
    public int YStride { get; }

    /// <summary>Gets the chroma row length (<see cref="YStride"/> / 2).</summary>
    public int UVStride { get; }

    /// <summary>Gets the padded luma plane (<see cref="YStride"/> x padded height).</summary>
    public Span<byte> Y => Get(_y).AsSpan(0, YStride * PaddedHeight);

    public Span<byte> U => Get(_u).AsSpan(0, UVStride * (PaddedHeight / 2));

    public Span<byte> V => Get(_v).AsSpan(0, UVStride * (PaddedHeight / 2));

    /// <summary>Gets the height padded to a multiple of 16.</summary>
    public int PaddedHeight => ((Height + 15) >> 4) * 16;

    /// <summary>Rents the planes of a <paramref name="width"/> x <paramref name="height"/> frame (zeroed).</summary>
    /// <exception cref="ImageResourceLimitException">The planes exceed the live-allocation limit.</exception>
    public static Vp8Planes Create(AllocationScope scope, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var stride = (long)((width + 15) >> 4) * 16;
        var paddedHeight = (long)((height + 15) >> 4) * 16;
        var lumaLength = stride * paddedHeight;
        if (lumaLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var chromaLength = (int)(lumaLength / 4);
        PooledBuffer? y = null;
        PooledBuffer? u = null;
        try
        {
            y = scope.Rent((int)lumaLength, AllocationKind.DecoderState);
            u = scope.Rent(chromaLength, AllocationKind.DecoderState);
            var v = scope.Rent(chromaLength, AllocationKind.DecoderState);
            return new Vp8Planes(width, height, y, u, v);
        }
        catch
        {
            y?.Dispose();
            u?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _y?.Dispose();
        _u?.Dispose();
        _v?.Dispose();
        _y = _u = _v = null;
    }

    private static byte[] Get(PooledBuffer? buffer) => buffer?.RawBuffer ?? throw new ObjectDisposedException(nameof(Vp8Planes));
}
