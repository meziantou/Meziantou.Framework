namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// An explicit raw pixel layout: channel order, sample precision and byte order. Buffers in these layouts are tightly
/// packed (row byte count = width x <see cref="BytesPerPixel"/>), top-down, with straight (non-premultiplied) alpha.
/// 16-bit samples are always little-endian, independently of the platform.
/// </summary>
public sealed class RawPixelLayout
{
    private RawPixelLayout(string name, string channels, int bytesPerSample)
    {
        Name = name;
        Channels = channels;
        BytesPerSample = bytesPerSample;
        AlphaChannel = channels.IndexOf('A', StringComparison.Ordinal);
    }

    /// <summary>Gets the 8-bit R, G, B, A layout (canonical for images of 8 bits or less).</summary>
    public static RawPixelLayout Rgba8 { get; } = new("rgba8", "RGBA", 1);

    /// <summary>Gets the 16-bit little-endian R, G, B, A layout (canonical for 16-bit images).</summary>
    public static RawPixelLayout Rgba16Le { get; } = new("rgba16le", "RGBA", 2);

    /// <summary>Gets the 8-bit R, G, B layout.</summary>
    public static RawPixelLayout Rgb8 { get; } = new("rgb8", "RGB", 1);

    /// <summary>Gets the 8-bit grayscale layout (native representation).</summary>
    public static RawPixelLayout Gray8 { get; } = new("gray8", "Y", 1);

    /// <summary>Gets the 16-bit little-endian grayscale layout (native representation).</summary>
    public static RawPixelLayout Gray16Le { get; } = new("gray16le", "Y", 2);

    /// <summary>Gets the 8-bit grayscale + alpha layout.</summary>
    public static RawPixelLayout GrayAlpha8 { get; } = new("graya8", "YA", 1);

    /// <summary>Gets the 16-bit little-endian grayscale + alpha layout.</summary>
    public static RawPixelLayout GrayAlpha16Le { get; } = new("graya16le", "YA", 2);

    /// <summary>Gets all the defined layouts.</summary>
    public static IReadOnlyList<RawPixelLayout> All { get; } = [Rgba8, Rgba16Le, Rgb8, Gray8, Gray16Le, GrayAlpha8, GrayAlpha16Le];

    /// <summary>Gets the manifest name of the layout (e.g. <c>rgba16le</c>).</summary>
    public string Name { get; }

    /// <summary>Gets the channel letters in storage order (e.g. <c>RGBA</c>, <c>Y</c>).</summary>
    public string Channels { get; }

    /// <summary>Gets the number of channels.</summary>
    public int ChannelCount => Channels.Length;

    /// <summary>Gets the number of bytes per sample (1 or 2).</summary>
    public int BytesPerSample { get; }

    /// <summary>Gets the number of bytes per pixel.</summary>
    public int BytesPerPixel => ChannelCount * BytesPerSample;

    /// <summary>Gets the index of the alpha channel, or -1.</summary>
    public int AlphaChannel { get; }

    /// <summary>Gets a value indicating whether the layout has an alpha channel.</summary>
    public bool HasAlpha => AlphaChannel >= 0;

    /// <summary>Gets a value indicating whether the layout has R, G and B channels.</summary>
    public bool IsColor => ChannelCount >= 3;

    /// <summary>Gets the maximum sample value (255 or 65535).</summary>
    public int MaxSampleValue => BytesPerSample == 1 ? byte.MaxValue : ushort.MaxValue;

    /// <summary>Gets the factor converting an 8-bit-scale error to this layout's scale (1 or 257).</summary>
    public int ScaleFrom8Bit => BytesPerSample == 1 ? 1 : 257;

    /// <summary>Finds a layout by its manifest name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="layout">The layout.</param>
    /// <returns><see langword="true"/> if the layout exists.</returns>
    public static bool TryParse(string? name, [NotNullWhen(true)] out RawPixelLayout? layout)
    {
        layout = All.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return layout is not null;
    }

    /// <summary>Finds a layout by its manifest name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException">Unknown layout.</exception>
    public static RawPixelLayout Parse(string name)
        => TryParse(name, out var layout) ? layout : throw new ArgumentException($"Unknown raw pixel layout '{name}'. Known layouts: {string.Join(", ", All)}.", nameof(name));

    /// <summary>Gets the name of a channel (e.g. <c>R</c>).</summary>
    /// <param name="channel">The channel index.</param>
    /// <returns>The channel name.</returns>
    public char GetChannelName(int channel) => Channels[channel];

    /// <summary>Gets the tightly packed row byte count for a width.</summary>
    /// <param name="width">The width.</param>
    /// <returns>The row byte count.</returns>
    public int GetRowBytes(int width) => checked(width * BytesPerPixel);

    /// <summary>Gets the exact buffer length for the given dimensions.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The buffer length.</returns>
    public int GetByteLength(int width, int height) => checked(GetRowBytes(width) * height);

    /// <summary>Formats a sample for diagnostics, keeping full numeric detail (hexadecimal included for 16-bit samples).</summary>
    /// <param name="value">The sample.</param>
    /// <returns>The formatted sample.</returns>
    public string FormatSample(int value)
        => BytesPerSample == 1
            ? value.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{value} (0x{value:X4})");

    /// <inheritdoc/>
    public override string ToString() => Name;
}
