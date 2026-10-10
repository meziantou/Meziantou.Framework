namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable physical resolution, in dots (pixels) per inch.</summary>
/// <remarks>
/// Formats storing a resolution in another unit (for example PNG pixels per meter) are converted on import
/// (<c>dpi = pixelsPerMeter * 0.0254</c>) and rounded to the format's integer unit on export, so decoded values round-trip
/// to the same encoded integers. Aspect-ratio-only resolutions without a physical unit are not represented.
/// </remarks>
public sealed class ImageResolution : IEquatable<ImageResolution>
{
    /// <summary>Initializes a new instance of the <see cref="ImageResolution"/> class.</summary>
    /// <param name="horizontalDpi">The horizontal resolution. Must be positive and finite.</param>
    /// <param name="verticalDpi">The vertical resolution. Must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">A resolution is zero, negative, infinite or NaN.</exception>
    public ImageResolution(double horizontalDpi, double verticalDpi)
    {
        if (!double.IsFinite(horizontalDpi) || horizontalDpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalDpi), horizontalDpi, "The resolution must be positive and finite.");

        if (!double.IsFinite(verticalDpi) || verticalDpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(verticalDpi), verticalDpi, "The resolution must be positive and finite.");

        HorizontalDpi = horizontalDpi;
        VerticalDpi = verticalDpi;
    }

    /// <summary>Gets the horizontal resolution, in dots per inch.</summary>
    public double HorizontalDpi { get; }

    /// <summary>Gets the vertical resolution, in dots per inch.</summary>
    public double VerticalDpi { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] ImageResolution? other) => other is not null && HorizontalDpi == other.HorizontalDpi && VerticalDpi == other.VerticalDpi;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ImageResolution);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(HorizontalDpi, VerticalDpi);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{HorizontalDpi}x{VerticalDpi} dpi");
}
