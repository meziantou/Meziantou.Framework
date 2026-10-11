using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// Builds a <see cref="RawPixelBuffer"/> row by row. This is the extension point used to copy decoded images into the
/// pure-buffer comparison model (wired to <c>Image</c>/<c>ImageFrame</c> rows by <see cref="Adapters.ImageSnapshots"/>): copy each row of
/// <c>ProcessPixelBytes</c>/<c>ProcessPixelRows</c> with <see cref="SetRow"/> (8-bit layouts) or <see cref="SetRow16"/>
/// (native-endian 16-bit samples such as <c>Rgba64</c>/<c>Gray16</c>, written little-endian).
/// </summary>
public sealed class RawPixelBufferBuilder
{
    private readonly byte[] _data;
    private bool _built;

    /// <summary>Initializes a new instance of the <see cref="RawPixelBufferBuilder"/> class (all samples zero).</summary>
    /// <param name="width">The width (positive).</param>
    /// <param name="height">The height (positive).</param>
    /// <param name="layout">The layout.</param>
    public RawPixelBufferBuilder(int width, int height, RawPixelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Layout = layout;
        _data = new byte[layout.GetByteLength(width, height)];
    }

    /// <summary>Gets the width.</summary>
    public int Width { get; }

    /// <summary>Gets the height.</summary>
    public int Height { get; }

    /// <summary>Gets the layout.</summary>
    public RawPixelLayout Layout { get; }

    /// <summary>Gets the row byte count.</summary>
    public int RowBytes => Layout.GetRowBytes(Width);

    /// <summary>Gets the writable bytes of a row (layout byte order).</summary>
    /// <param name="y">The row.</param>
    /// <returns>The row bytes.</returns>
    public Span<byte> GetRowSpan(int y)
    {
        EnsureNotBuilt();
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return _data.AsSpan(y * RowBytes, RowBytes);
    }

    /// <summary>Copies a row given in the layout byte order. The row length must be exactly <see cref="RowBytes"/>.</summary>
    /// <param name="y">The row.</param>
    /// <param name="row">The row bytes.</param>
    public void SetRow(int y, ReadOnlySpan<byte> row)
    {
        var destination = GetRowSpan(y);
        if (row.Length != destination.Length)
            throw new ArgumentException($"Row {y.ToString(CultureInfo.InvariantCulture)} has {row.Length.ToString(CultureInfo.InvariantCulture)} bytes; {Width.ToString(CultureInfo.InvariantCulture)} {Layout} pixels require exactly {destination.Length.ToString(CultureInfo.InvariantCulture)} bytes.", nameof(row));

        row.CopyTo(destination);
    }

    /// <summary>Copies a row of native-endian 16-bit samples (e.g. <c>Rgba64</c> components), stored little-endian.</summary>
    /// <param name="y">The row.</param>
    /// <param name="samples">The samples; exactly width x channels.</param>
    public void SetRow16(int y, ReadOnlySpan<ushort> samples)
    {
        if (Layout.BytesPerSample != 2)
            throw new InvalidOperationException($"SetRow16 requires a 16-bit layout; the layout is {Layout}.");

        var destination = GetRowSpan(y);
        if (samples.Length * 2 != destination.Length)
            throw new ArgumentException($"Row {y.ToString(CultureInfo.InvariantCulture)} has {samples.Length.ToString(CultureInfo.InvariantCulture)} samples; {Width.ToString(CultureInfo.InvariantCulture)} {Layout} pixels require exactly {(destination.Length / 2).ToString(CultureInfo.InvariantCulture)} samples.", nameof(samples));

        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], samples[i]);
        }
    }

    /// <summary>Sets one sample.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="value">The value.</param>
    public void SetSample(int x, int y, int channel, int value)
    {
        EnsureNotBuilt();
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, Layout.ChannelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Layout.MaxSampleValue);
        RawPixelBuffer.SetSampleAt(_data, Layout, (((y * Width) + x) * Layout.ChannelCount) + channel, value);
    }

    /// <summary>Completes the buffer. The builder cannot be used afterward.</summary>
    /// <returns>The buffer.</returns>
    public RawPixelBuffer Build()
    {
        EnsureNotBuilt();
        _built = true;
        return RawPixelBuffer.Wrap(Width, Height, Layout, _data);
    }

    private void EnsureNotBuilt()
    {
        if (_built)
            throw new InvalidOperationException("The buffer was already built.");
    }
}
