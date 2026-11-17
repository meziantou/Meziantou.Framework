namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The immutable variables of the information header of a Radiance HDR file, and the order of its scanlines. This is
/// informational only, like <see cref="ImageMetadata.SourceFormat"/>: it describes the file, not the pixels.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is applied to the pixels: they are the values the file stores. The radiances the picture had before it was
/// adjusted are the pixels divided by <see cref="Exposure"/>, and each color divided by its color correction too.
/// </para>
/// <para>
/// No operation updates it, and every encoder ignores it: <see cref="Formats.RadianceEncoder"/> writes no exposure and no
/// color correction, since the pixels it writes are the values it is given.
/// </para>
/// </remarks>
public sealed class RadianceHeader : IEquatable<RadianceHeader>
{
    /// <summary>Initializes a new instance of the <see cref="RadianceHeader"/> class.</summary>
    /// <param name="isXyz">Whether the samples are CIE XYZ (<c>FORMAT=32-bit_rle_xyze</c>) rather than RGB.</param>
    /// <param name="exposure">The product of the <c>EXPOSURE</c> variables, 1 without any.</param>
    /// <param name="colorCorrection">The products of the three terms of the <c>COLORCORR</c> variables, (1, 1, 1) without any.</param>
    /// <param name="primaries">The eight chromaticities of the <c>PRIMARIES</c> variable, or <see langword="null"/> without it.</param>
    /// <param name="pixelAspectRatio">The product of the <c>PIXASPECT</c> variables, 1 without any.</param>
    /// <param name="gamma">The value of the <c>GAMMA</c> variable, or <see langword="null"/> without it.</param>
    /// <param name="view">The <c>VIEW</c> variables, joined with a space, or <see langword="null"/> without any.</param>
    /// <param name="software">The value of the <c>SOFTWARE</c> variable, or <see langword="null"/> without it.</param>
    /// <param name="scanOrder">The orientation of the resolution string, such as <c>-Y +X</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="colorCorrection"/> or <paramref name="scanOrder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="colorCorrection"/> does not have three terms, <paramref name="primaries"/> does not have eight, or <paramref name="scanOrder"/> is not one of the eight orientations.</exception>
    public RadianceHeader(bool isXyz, double exposure, IReadOnlyList<double> colorCorrection, IReadOnlyList<double>? primaries, double pixelAspectRatio, double? gamma, string? view, string? software, string scanOrder)
    {
        ArgumentNullException.ThrowIfNull(colorCorrection);
        ArgumentNullException.ThrowIfNull(scanOrder);
        if (colorCorrection.Count != 3)
            throw new ArgumentException("The color correction has three terms.", nameof(colorCorrection));

        if (primaries is { Count: not 8 })
            throw new ArgumentException("The primaries are the eight chromaticities of red, green, blue and white.", nameof(primaries));

        if (scanOrder is not ("-Y +X" or "-Y -X" or "+Y -X" or "+Y +X" or "+X +Y" or "-X +Y" or "-X -Y" or "+X -Y"))
            throw new ArgumentException("The scan order is not one of the eight orientations of a resolution string.", nameof(scanOrder));

        IsXyz = isXyz;
        Exposure = exposure;
        ColorCorrection = Array.AsReadOnly([.. colorCorrection]);
        Primaries = primaries is null ? null : Array.AsReadOnly([.. primaries]);
        PixelAspectRatio = pixelAspectRatio;
        Gamma = gamma;
        View = view;
        Software = software;
        ScanOrder = scanOrder;
    }

    /// <summary>Gets a value indicating whether the samples of the file are CIE XYZ (<c>32-bit_rle_xyze</c>) rather than RGB (<c>32-bit_rle_rgbe</c>).</summary>
    public bool IsXyz { get; }

    /// <summary>Gets the product of the <c>EXPOSURE</c> variables: the multiplier that was applied to the pixels (1 without any).</summary>
    public double Exposure { get; }

    /// <summary>Gets the products of the red, green and blue terms of the <c>COLORCORR</c> variables ((1, 1, 1) without any).</summary>
    public IReadOnlyList<double> ColorCorrection { get; }

    /// <summary>
    /// Gets the CIE (x, y) chromaticities of the red, green and blue primaries and of the white point of the <c>PRIMARIES</c>
    /// variable, or <see langword="null"/> without it (the standard primaries of Radiance).
    /// </summary>
    public IReadOnlyList<double>? Primaries { get; }

    /// <summary>Gets the product of the <c>PIXASPECT</c> variables: the height of a pixel divided by its width (1 without any).</summary>
    public double PixelAspectRatio { get; }

    /// <summary>Gets the value of the <c>GAMMA</c> variable, or <see langword="null"/> without it. It is never applied.</summary>
    public double? Gamma { get; }

    /// <summary>Gets the view parameters of the <c>VIEW</c> variables, joined with a space, or <see langword="null"/> without any.</summary>
    public string? View { get; }

    /// <summary>Gets the value of the <c>SOFTWARE</c> variable, or <see langword="null"/> without it.</summary>
    public string? Software { get; }

    /// <summary>
    /// Gets the orientation of the resolution string of the file, without its sizes: <c>-Y +X</c> for scanlines that are rows
    /// stored from the top, left to right (the standard order), or one of the seven other orders (<c>-Y -X</c>, <c>+Y -X</c>,
    /// <c>+Y +X</c>, <c>+X +Y</c>, <c>-X +Y</c>, <c>-X -Y</c>, <c>+X -Y</c>). The decoded image is always in the standard
    /// orientation.
    /// </summary>
    public string ScanOrder { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] RadianceHeader? other)
        => other is not null
            && IsXyz == other.IsXyz
            && Exposure.Equals(other.Exposure)
            && ColorCorrection.SequenceEqual(other.ColorCorrection)
            && (Primaries is null ? other.Primaries is null : other.Primaries is not null && Primaries.SequenceEqual(other.Primaries))
            && PixelAspectRatio.Equals(other.PixelAspectRatio)
            && Nullable.Equals(Gamma, other.Gamma)
            && string.Equals(View, other.View, StringComparison.Ordinal)
            && string.Equals(Software, other.Software, StringComparison.Ordinal)
            && string.Equals(ScanOrder, other.ScanOrder, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as RadianceHeader);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsXyz, Exposure, PixelAspectRatio, Gamma, View, Software, ScanOrder);

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{(IsXyz ? "XYZE" : "RGBE")} {ScanOrder}, exposure {Exposure}");
}
