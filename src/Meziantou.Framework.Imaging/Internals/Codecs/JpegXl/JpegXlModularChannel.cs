namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One channel of a Modular image (ISO/IEC 18181-1, section H.2): a plane of 32-bit signed samples, and the factor by
/// which it is subsampled with respect to the image. A meta-channel (the palette of a palette transform) has no position
/// in the image: its shifts are -1.
/// </summary>
internal sealed class JpegXlModularChannel : IDisposable
{
    private Av1PooledArray<int>? _data;

    public JpegXlModularChannel(int width, int height, int horizontalShift, int verticalShift)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
        HorizontalShift = horizontalShift;
        VerticalShift = verticalShift;
    }

    /// <summary>Gets the number of samples of a row.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the number of rows.</summary>
    public int Height { get; private set; }

    /// <summary>Gets or sets the base-2 logarithm of the horizontal subsampling.</summary>
    public int HorizontalShift { get; set; }

    /// <summary>Gets or sets the base-2 logarithm of the vertical subsampling.</summary>
    public int VerticalShift { get; set; }

    /// <summary>Gets a value indicating whether the channel has no sample.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Gets the samples, row by row. <see cref="Allocate"/> must have been called.</summary>
    public Span<int> Samples => _data is null ? default : _data.Span;

    /// <summary>Gets the samples of a row.</summary>
    public Span<int> GetRow(int y) => Samples.Slice(y * Width, Width);

    /// <summary>Changes the size of a channel that has no samples yet.</summary>
    public void Resize(int width, int height)
    {
        if (_data is not null)
            throw new InvalidOperationException("The channel is already allocated.");

        Width = width;
        Height = height;
    }

    /// <summary>Rents the samples of the channel, zeroed, unless it already has them.</summary>
    public void Allocate(AllocationScope scope)
    {
        _data ??= Av1PooledArray<int>.Rent(scope, (long)Width * Height, clear: true);
    }

    /// <summary>Replaces the content of this channel with the content of <paramref name="other"/>, which becomes empty.</summary>
    public void TakeFrom(JpegXlModularChannel other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _data?.Dispose();
        _data = other._data;
        other._data = null;
        Width = other.Width;
        Height = other.Height;
        HorizontalShift = other.HorizontalShift;
        VerticalShift = other.VerticalShift;
    }

    public void Dispose()
    {
        _data?.Dispose();
        _data = null;
    }
}
