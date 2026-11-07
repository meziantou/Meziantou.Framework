using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The parameters of a gain map (ISO 21496-1, the <c>hdrgm</c> XMP namespace of Adobe and Ultra HDR): an image stored next
/// to a base image, which gives the gain that turns the base image into an alternate rendition, typically a standard
/// dynamic range photo into its high dynamic range version.
/// </summary>
/// <remarks>
/// <para>
/// The headrooms are in stops: the log2 of the ratio of the peak light of a rendition to the white of standard dynamic
/// range. A display of headroom <c>H</c> shows the base image when <c>H</c> is at most
/// <see cref="BaseHdrHeadroom"/>, the alternate image when <c>H</c> is at least <see cref="AlternateHdrHeadroom"/>, and
/// an interpolation in the log domain between them (<see cref="GetWeight(double)"/>).
/// </para>
/// <para>
/// A file stores the values as fractions (ISO 21496-1) or decimal numbers (XMP): they are converted to
/// <see cref="double"/>, and an Ultra HDR description with a high dynamic range base rendition
/// (<c>BaseRenditionIsHDR</c>) is expressed with the headrooms and the offsets of the base and alternate renditions
/// swapped, which gives the same colors.
/// </para>
/// </remarks>
public sealed class GainMapMetadata : IEquatable<GainMapMetadata>
{
    private readonly GainMapChannel[] _channels;

    /// <summary>Initializes a new instance of the <see cref="GainMapMetadata"/> class.</summary>
    /// <param name="baseHdrHeadroom">The headroom of the base rendition, in stops; 0 for a standard dynamic range base image.</param>
    /// <param name="alternateHdrHeadroom">The headroom of the alternate rendition, in stops.</param>
    /// <param name="channels">The parameters of the channels: one, for the three color channels, or three (red, green and blue).</param>
    /// <param name="useBaseColorSpace">
    /// <see langword="true"/> when the gain applies to the linear colors of the base image, <see langword="false"/> when it
    /// applies to their conversion to the color space of the alternate rendition.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="channels"/> or one of its values is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="channels"/> does not have one or three values.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A headroom is negative or not finite.</exception>
    public GainMapMetadata(double baseHdrHeadroom, double alternateHdrHeadroom, IEnumerable<GainMapChannel> channels, bool useBaseColorSpace = true)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (!double.IsFinite(baseHdrHeadroom) || baseHdrHeadroom < 0)
            throw new ArgumentOutOfRangeException(nameof(baseHdrHeadroom), baseHdrHeadroom, "The headroom must be a finite number of at least 0.");

        if (!double.IsFinite(alternateHdrHeadroom) || alternateHdrHeadroom < 0)
            throw new ArgumentOutOfRangeException(nameof(alternateHdrHeadroom), alternateHdrHeadroom, "The headroom must be a finite number of at least 0.");

        var values = channels.ToArray();
        if (values.Length is not (1 or 3))
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A gain map has one or three channels, not {values.Length}."), nameof(channels));

        foreach (var channel in values)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channels), "A channel is null.");
        }

        BaseHdrHeadroom = baseHdrHeadroom;
        AlternateHdrHeadroom = alternateHdrHeadroom;
        _channels = values;
        UseBaseColorSpace = useBaseColorSpace;
    }

    /// <summary>Gets the headroom of the base rendition, in stops; 0 for a standard dynamic range base image.</summary>
    public double BaseHdrHeadroom { get; }

    /// <summary>Gets the headroom of the alternate rendition, in stops.</summary>
    public double AlternateHdrHeadroom { get; }

    /// <summary>Gets the parameters of the channels: one, for the three color channels, or three (red, green and blue).</summary>
    public IReadOnlyList<GainMapChannel> Channels => _channels;

    /// <summary>
    /// Gets a value indicating whether the gain applies to the linear colors of the base image (<see langword="true"/>),
    /// or to their conversion to the color space of the alternate rendition.
    /// </summary>
    public bool UseBaseColorSpace { get; }

    /// <summary>
    /// Gets how much of the gain a display of a given headroom applies: 0 shows the base rendition, 1 the alternate rendition.
    /// </summary>
    /// <param name="hdrHeadroom">The headroom of the display, in stops: the log2 of the ratio of its peak light to the white of standard dynamic range (0 for a standard dynamic range display).</param>
    /// <returns>
    /// <c>(hdrHeadroom - BaseHdrHeadroom) / (AlternateHdrHeadroom - BaseHdrHeadroom)</c> clamped to [0, 1]; when the two
    /// headrooms are equal, 1 from that headroom on and 0 below it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hdrHeadroom"/> is negative or not finite.</exception>
    public double GetWeight(double hdrHeadroom)
    {
        if (!double.IsFinite(hdrHeadroom) || hdrHeadroom < 0)
            throw new ArgumentOutOfRangeException(nameof(hdrHeadroom), hdrHeadroom, "The headroom must be a finite number of at least 0.");

        if (AlternateHdrHeadroom == BaseHdrHeadroom)
            return hdrHeadroom >= AlternateHdrHeadroom ? 1 : 0;

        return Math.Clamp((hdrHeadroom - BaseHdrHeadroom) / (AlternateHdrHeadroom - BaseHdrHeadroom), 0, 1);
    }

    /// <summary>
    /// Applies a gain map to a base image: computes the rendition of the image for a display of a given headroom, in linear
    /// light, with the values above 1 that high dynamic range colors have.
    /// </summary>
    /// <param name="baseImage">The base image: its first frame is used.</param>
    /// <param name="gainMap">The gain map image: its first frame is used. A grayscale image applies its gain to the three channels; it may be smaller than the base image, which it then covers with bilinear interpolation.</param>
    /// <param name="hdrHeadroom">The headroom of the display, in stops (<see cref="GetWeight(double)"/>).</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>
    /// A new image of the size of the base image whose colors are linear light, 1 being the white of standard dynamic range,
    /// in the primaries of the base image. Nothing is clipped: colors may exceed 1, and an offset may make a channel slightly
    /// negative. Alpha is the one of the base image. The metadata is labeled accordingly: linear sRGB primaries are
    /// <see cref="ColorTransferFunction.Linear"/>, the other primaries a CICP description with the linear transfer
    /// characteristics (8); the orientation and the EXIF and XMP metadata of the base image are kept.
    /// </returns>
    /// <remarks>
    /// The base image is converted to linear light with its color description: an RGB ICC profile whose primaries are
    /// those of sRGB, Display P3, DCI-P3 or BT.2020, a CICP description whose transfer characteristics are not high dynamic
    /// range ones, the linear transfer function, or else sRGB. The samples of both images are read with 16-bit precision,
    /// and the samples of the gain map are its coded values, whatever its color description.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="baseImage"/> or <paramref name="gainMap"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hdrHeadroom"/> is negative or not finite.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The color description of the base image is not one of those above, or <see cref="UseBaseColorSpace"/> is
    /// <see langword="false"/>: the color space of the alternate rendition is not known here
    /// (<see cref="AuxiliaryImageCollection.DecodeHdr(double, ImageDecodeOptions?, CancellationToken)"/> reads it from the file).
    /// </exception>
    /// <exception cref="ObjectDisposedException">An image is disposed.</exception>
    /// <exception cref="ImageResourceLimitException">The result exceeds the allocation limit of the configuration of the base image.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public Image<RgbaFloat> Apply(Image baseImage, Image gainMap, double hdrHeadroom, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(gainMap);
        var weight = GetWeight(hdrHeadroom);
        return GainMapRenderer.Render(baseImage, gainMap, this, weight, alternatePrimaries: null, cancellationToken);
    }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] GainMapMetadata? other)
    {
        return other is not null
            && BaseHdrHeadroom.Equals(other.BaseHdrHeadroom)
            && AlternateHdrHeadroom.Equals(other.AlternateHdrHeadroom)
            && UseBaseColorSpace == other.UseBaseColorSpace
            && _channels.AsSpan().SequenceEqual(other._channels);
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as GainMapMetadata);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(BaseHdrHeadroom);
        hash.Add(AlternateHdrHeadroom);
        hash.Add(UseBaseColorSpace);
        foreach (var channel in _channels)
        {
            hash.Add(channel);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"headroom {BaseHdrHeadroom} to {AlternateHdrHeadroom} stops, {_channels.Length} channel(s): {string.Join("; ", (IEnumerable<GainMapChannel>)_channels)}");
}
