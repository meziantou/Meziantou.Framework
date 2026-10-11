namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The samples of a frame or of the image as floating-point values, where 1 is the largest value of an integer sample:
/// the color channels (one for a grayscale image without color transform, otherwise three), then the extra channels.
/// </summary>
internal sealed class JpegXlImageBuffer : IDisposable
{
    private readonly Av1PooledArray<float>?[] _planes;
    private readonly int[] _strides;

    /// <summary>Creates a buffer without samples: each channel is allocated or adopted afterwards.</summary>
    public JpegXlImageBuffer(int width, int height, int colorChannelCount, int extraChannelCount)
    {
        Width = width;
        Height = height;
        ColorChannelCount = colorChannelCount;
        _planes = new Av1PooledArray<float>?[colorChannelCount + extraChannelCount];
        _strides = new int[_planes.Length];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the number of color channels.</summary>
    public int ColorChannelCount { get; }

    /// <summary>Gets the number of channels: the color channels, then the extra channels.</summary>
    public int ChannelCount => _planes.Length;

    /// <summary>Gets or sets a value indicating whether the color channels are samples that the color transform of the image has not converted.</summary>
    public bool IsBeforeColorTransform { get; set; }

    /// <summary>Allocates the samples of a channel.</summary>
    public void Allocate(int channel, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _planes[channel]?.Dispose();
        _planes[channel] = null;
        _planes[channel] = Av1PooledArray<float>.Rent(scope, (long)Width * Height, clear: false);
        _strides[channel] = Width;
    }

    /// <summary>Takes the samples of a channel, in rows of <paramref name="stride"/> values; the buffer disposes them.</summary>
    public void Adopt(int channel, Av1PooledArray<float> samples, int stride)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, Width);
        _planes[channel]?.Dispose();
        _planes[channel] = samples;
        _strides[channel] = stride;
    }

    /// <summary>Gets consecutive rows of a channel that was allocated by the buffer: they follow each other without gap.</summary>
    public Span<float> GetRow(int channel, int y, int rowCount)
    {
        if (_strides[channel] != Width)
            throw new InvalidOperationException("The rows of the channel do not follow each other.");

        return _planes[channel]!.Span.Slice(y * Width, rowCount * Width);
    }

    /// <summary>Gets a row of a channel.</summary>
    public Span<float> GetRow(int channel, int y) => _planes[channel]!.Span.Slice(y * _strides[channel], Width);

    /// <summary>Gets the samples of a channel, in rows of <see cref="GetStride"/> values.</summary>
    public ReadOnlySpan<float> GetPlane(int channel) => _planes[channel]!.Span;

    /// <summary>Gets the distance between the rows of a channel.</summary>
    public int GetStride(int channel) => _strides[channel];

    /// <summary>Releases the samples of a channel that is not read any more.</summary>
    public void Release(int channel)
    {
        _planes[channel]?.Dispose();
        _planes[channel] = null;
    }

    /// <summary>Creates a copy of the buffer.</summary>
    public JpegXlImageBuffer Clone(AllocationScope scope)
    {
        JpegXlImageBuffer? owned = null;
        try
        {
            var result = owned = new JpegXlImageBuffer(Width, Height, ColorChannelCount, ChannelCount - ColorChannelCount) { IsBeforeColorTransform = IsBeforeColorTransform };
            for (var c = 0; c < _planes.Length; c++)
            {
                result.Allocate(c, scope);
                for (var y = 0; y < Height; y++)
                {
                    GetRow(c, y).CopyTo(result.GetRow(c, y));
                }
            }

            owned = null;
            return result;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < _planes.Length; i++)
        {
            _planes[i]?.Dispose();
            _planes[i] = null;
        }
    }
}
