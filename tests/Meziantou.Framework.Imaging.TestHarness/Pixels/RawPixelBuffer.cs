using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// An immutable, tightly packed, top-down pixel buffer in an explicit <see cref="RawPixelLayout"/>. It is the pure-buffer
/// side of every golden comparison: references are read into it from raw files, and decoded images are copied into it
/// row by row (see <see cref="RawPixelBufferBuilder"/>), never through an encoded image format.
/// </summary>
public sealed class RawPixelBuffer
{
    private readonly byte[] _data;

    private RawPixelBuffer(int width, int height, RawPixelLayout layout, byte[] data)
    {
        Width = width;
        Height = height;
        Layout = layout;
        _data = data;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the layout.</summary>
    public RawPixelLayout Layout { get; }

    /// <summary>Gets the row byte count.</summary>
    public int RowBytes => Layout.GetRowBytes(Width);

    /// <summary>Gets the buffer length.</summary>
    public int ByteLength => _data.Length;

    /// <summary>Gets the number of samples (pixels x channels).</summary>
    public int SampleCount => Width * Height * Layout.ChannelCount;

    /// <summary>Gets the bytes.</summary>
    public ReadOnlySpan<byte> Span => _data;

    /// <summary>Gets the bytes.</summary>
    public ReadOnlyMemory<byte> Memory => _data;

    /// <summary>Creates a buffer from tightly packed bytes (copied). The length must be exact.</summary>
    /// <param name="width">The width (positive).</param>
    /// <param name="height">The height (positive).</param>
    /// <param name="layout">The layout.</param>
    /// <param name="data">The bytes.</param>
    /// <returns>The buffer.</returns>
    /// <exception cref="ArgumentException">The length does not match the dimensions and layout (truncated or oversized buffer).</exception>
    public static RawPixelBuffer Create(int width, int height, RawPixelLayout layout, ReadOnlySpan<byte> data) => Create(width, height, layout, data, stride: 0);

    /// <summary>Creates a buffer from rows separated by a stride (only the visible bytes of each row are copied).</summary>
    /// <param name="width">The width (positive).</param>
    /// <param name="height">The height (positive).</param>
    /// <param name="layout">The layout.</param>
    /// <param name="data">The bytes.</param>
    /// <param name="stride">The distance in bytes between rows; 0 means tightly packed. The last row only needs the visible bytes.</param>
    /// <returns>The buffer.</returns>
    /// <exception cref="ArgumentException">The length or stride does not match the dimensions and layout.</exception>
    public static RawPixelBuffer Create(int width, int height, RawPixelLayout layout, ReadOnlySpan<byte> data, int stride)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = layout.GetRowBytes(width);
        var expected = layout.GetByteLength(width, height);
        if (stride == 0)
        {
            if (data.Length != expected)
                throw new ArgumentException(DescribeLengthMismatch(width, height, layout, data.Length), nameof(data));

            return new RawPixelBuffer(width, height, layout, data.ToArray());
        }

        if (stride < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, $"The stride must be at least the row byte count ({rowBytes.ToString(CultureInfo.InvariantCulture)}).");

        var required = checked(((long)stride * (height - 1)) + rowBytes);
        if (data.Length < required)
            throw new ArgumentException($"The buffer has {data.Length.ToString(CultureInfo.InvariantCulture)} bytes but {required.ToString(CultureInfo.InvariantCulture)} are required for {width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)} {layout} rows with a stride of {stride.ToString(CultureInfo.InvariantCulture)}.", nameof(data));

        var result = new byte[expected];
        for (var y = 0; y < height; y++)
        {
            data.Slice(y * stride, rowBytes).CopyTo(result.AsSpan(y * rowBytes));
        }

        return new RawPixelBuffer(width, height, layout, result);
    }

    /// <summary>Describes a length mismatch (truncated or oversized buffer) with the exact expected layout.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="actualLength">The actual length.</param>
    /// <returns>The description.</returns>
    public static string DescribeLengthMismatch(int width, int height, RawPixelLayout layout, long actualLength)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var expected = layout.GetByteLength(width, height);
        var kind = actualLength < expected ? "truncated" : "oversized";
        return string.Create(CultureInfo.InvariantCulture,
            $"The buffer has {actualLength} bytes but {width}x{height} {layout} requires exactly {expected} bytes ({height} rows of {layout.GetRowBytes(width)} bytes): {kind} by {Math.Abs(actualLength - expected)} bytes.");
    }

    /// <summary>Gets the bytes of a row.</summary>
    /// <param name="y">The row index.</param>
    /// <returns>The row bytes.</returns>
    public ReadOnlySpan<byte> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return _data.AsSpan(y * RowBytes, RowBytes);
    }

    /// <summary>Gets a sample value (16-bit samples are read as little-endian).</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="channel">The channel index in <see cref="RawPixelLayout.Channels"/>.</param>
    /// <returns>The sample value.</returns>
    public int GetSample(int x, int y, int channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, Layout.ChannelCount);
        return GetSampleAt((((y * Width) + x) * Layout.ChannelCount) + channel);
    }

    /// <summary>Gets a copy of the bytes.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray() => (byte[])_data.Clone();

    /// <summary>Returns a copy with one sample changed (useful to prove that comparisons detect corruption).</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="value">The new value.</param>
    /// <returns>The modified copy.</returns>
    public RawPixelBuffer WithSample(int x, int y, int channel, int value)
    {
        _ = GetSample(x, y, channel);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Layout.MaxSampleValue);
        var copy = ToArray();
        SetSampleAt(copy, Layout, (((y * Width) + x) * Layout.ChannelCount) + channel, value);
        return new RawPixelBuffer(Width, Height, Layout, copy);
    }

    /// <summary>Returns a copy with two channels exchanged.</summary>
    /// <param name="first">The first channel.</param>
    /// <param name="second">The second channel.</param>
    /// <returns>The modified copy.</returns>
    public RawPixelBuffer SwapChannels(int first, int second)
    {
        var channels = Layout.ChannelCount;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(first, channels);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(second, channels);
        var copy = ToArray();
        for (var sample = 0; sample < SampleCount; sample += channels)
        {
            var a = GetSampleAt(sample + first);
            var b = GetSampleAt(sample + second);
            SetSampleAt(copy, Layout, sample + first, b);
            SetSampleAt(copy, Layout, sample + second, a);
        }

        return new RawPixelBuffer(Width, Height, Layout, copy);
    }

    /// <summary>Returns a copy where every sample of a channel has the specified value (e.g. alpha forced to opaque).</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="value">The value.</param>
    /// <returns>The modified copy.</returns>
    public RawPixelBuffer WithChannel(int channel, int value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, Layout.ChannelCount);
        var copy = ToArray();
        for (var sample = channel; sample < SampleCount; sample += Layout.ChannelCount)
        {
            SetSampleAt(copy, Layout, sample, value);
        }

        return new RawPixelBuffer(Width, Height, Layout, copy);
    }

    /// <summary>Returns a copy where the color of fully transparent pixels is set to zero (for tools that do not define hidden colors).</summary>
    /// <returns>The modified copy.</returns>
    public RawPixelBuffer WithTransparentColorsCleared()
    {
        if (!Layout.HasAlpha)
            return this;

        var copy = ToArray();
        var channels = Layout.ChannelCount;
        for (var sample = 0; sample < SampleCount; sample += channels)
        {
            if (GetSampleAt(sample + Layout.AlphaChannel) != 0)
                continue;

            for (var c = 0; c < channels; c++)
            {
                SetSampleAt(copy, Layout, sample + c, 0);
            }
        }

        return new RawPixelBuffer(Width, Height, Layout, copy);
    }

    /// <summary>Returns a vertically flipped copy (rows reversed).</summary>
    /// <returns>The flipped copy.</returns>
    public RawPixelBuffer FlipVertical() => Transform(Width, Height, (x, y) => (x, Height - 1 - y));

    /// <summary>Returns a horizontally flipped copy (columns reversed).</summary>
    /// <returns>The flipped copy.</returns>
    public RawPixelBuffer FlipHorizontal() => Transform(Width, Height, (x, y) => (Width - 1 - x, y));

    /// <summary>Returns a copy rotated by 180 degrees.</summary>
    /// <returns>The rotated copy.</returns>
    public RawPixelBuffer Rotate180() => Transform(Width, Height, (x, y) => (Width - 1 - x, Height - 1 - y));

    /// <summary>Returns a copy rotated by 90 degrees clockwise (dimensions swapped).</summary>
    /// <returns>The rotated copy.</returns>
    public RawPixelBuffer Rotate90Clockwise() => Transform(Height, Width, (x, y) => (y, Height - 1 - x));

    /// <summary>Returns a copy rotated by 90 degrees counterclockwise (dimensions swapped).</summary>
    /// <returns>The rotated copy.</returns>
    public RawPixelBuffer Rotate90CounterClockwise() => Transform(Height, Width, (x, y) => (Width - 1 - y, x));

    /// <summary>Returns the transposed copy (dimensions swapped).</summary>
    /// <returns>The transposed copy.</returns>
    public RawPixelBuffer Transpose() => Transform(Height, Width, (x, y) => (y, x));

    /// <summary>Returns the transversed copy: the reflection across the anti-diagonal (dimensions swapped; EXIF orientation 7).</summary>
    /// <returns>The transversed copy.</returns>
    public RawPixelBuffer Transverse() => Transform(Height, Width, (x, y) => (Width - 1 - y, Height - 1 - x));

    /// <summary>Returns a copy of a rectangle of this buffer.</summary>
    /// <param name="x">The left column of the rectangle.</param>
    /// <param name="y">The top row of the rectangle.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <returns>The cropped copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The rectangle is empty or not entirely inside the buffer.</exception>
    public RawPixelBuffer Crop(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(x + width, Width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(y + height, Height, nameof(height));
        return Transform(width, height, (dx, dy) => (x + dx, y + dy));
    }

    /// <summary>
    /// Returns a copy of a rectangle that may reach outside this buffer: the pixels of the rectangle inside the buffer are
    /// copied, the others get <paramref name="fill"/>.
    /// </summary>
    /// <param name="x">The left column of the rectangle (may be negative).</param>
    /// <param name="y">The top row of the rectangle (may be negative).</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <param name="fill">The samples of the pixels outside this buffer, one per channel of the layout.</param>
    /// <returns>The extended copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The rectangle is empty.</exception>
    /// <exception cref="ArgumentException"><paramref name="fill"/> does not have one sample per channel.</exception>
    public RawPixelBuffer Extend(int x, int y, int width, int height, ReadOnlySpan<int> fill)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var channels = Layout.ChannelCount;
        if (fill.Length != channels)
            throw new ArgumentException($"The fill must have {channels} samples.", nameof(fill));

        var result = new byte[Layout.GetByteLength(width, height)];
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                var sx = (long)x + dx;
                var sy = (long)y + dy;
                var inside = sx >= 0 && sx < Width && sy >= 0 && sy < Height;
                for (var c = 0; c < channels; c++)
                {
                    SetSampleAt(result, Layout, (((dy * width) + dx) * channels) + c, inside ? GetSample((int)sx, (int)sy, c) : fill[c]);
                }
            }
        }

        return new RawPixelBuffer(width, height, Layout, result);
    }

    internal int GetSampleAt(int sampleIndex)
        => Layout.BytesPerSample == 1 ? _data[sampleIndex] : BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(sampleIndex * 2));

    internal static void SetSampleAt(Span<byte> data, RawPixelLayout layout, int sampleIndex, int value)
    {
        if (layout.BytesPerSample == 1)
        {
            data[sampleIndex] = checked((byte)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data[(sampleIndex * 2)..], checked((ushort)value));
        }
    }

    internal static RawPixelBuffer Wrap(int width, int height, RawPixelLayout layout, byte[] data)
    {
        if (data.Length != layout.GetByteLength(width, height))
            throw new ArgumentException(DescribeLengthMismatch(width, height, layout, data.Length), nameof(data));

        return new RawPixelBuffer(width, height, layout, data);
    }

    /// <summary>Builds a buffer of the specified size where pixel (x, y) is copied from <c>source(x, y)</c> of this buffer.</summary>
    private RawPixelBuffer Transform(int width, int height, Func<int, int, (int X, int Y)> source)
    {
        var bpp = Layout.BytesPerPixel;
        var result = new byte[Layout.GetByteLength(width, height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (sx, sy) = source(x, y);
                _data.AsSpan(((sy * Width) + sx) * bpp, bpp).CopyTo(result.AsSpan(((y * width) + x) * bpp));
            }
        }

        return new RawPixelBuffer(width, height, Layout, result);
    }
}
