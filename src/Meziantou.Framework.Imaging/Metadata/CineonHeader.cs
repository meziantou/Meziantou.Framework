namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The fields of the header of a Kodak Cineon file (Image File Format Proposal for Digital Pictures, draft 4.5) that
/// describe its channels and where it comes from. This is informational only, like <see cref="ImageMetadata.SourceFormat"/>:
/// it describes the file, not the pixels.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is applied to the pixels: they are the code values the file stores, scaled to the sample range of the pixel
/// format. Cineon files usually hold printing densities (a logarithmic film scan); the minimum and maximum quantities say
/// which densities the code values represent, and a conversion to linear light is an explicit operation of the caller.
/// </para>
/// <para>
/// A field the file leaves undefined (the "undefined" value of its type: all bits set for unsigned numbers, 0x80000000 for
/// signed ones, positive infinity for reals and an empty string for text) is <see langword="null"/>. No operation updates
/// it. <see cref="Formats.CineonEncoder"/> copies its fields to the files it writes (the byte order and the packing are
/// those of the encoder); the other encoders ignore it.
/// </para>
/// </remarks>
public sealed class CineonHeader : IEquatable<CineonHeader>
{
    /// <summary>Gets the version of the header format, such as <c>V4.5</c>.</summary>
    public string Version { get; init; } = "";

    /// <summary>Gets a value indicating whether the file is big-endian (magic number <c>80 2A 5F D7</c>) rather than little-endian.</summary>
    public bool IsBigEndian { get; init; }

    /// <summary>
    /// Gets the designator of each channel, in the order of the file: the metric (byte 0, 0 for the universal metric) in the
    /// high byte and the code (byte 1) in the low byte. In the universal metric, 0 is black and white, 1 to 3 are the red,
    /// green and blue printing densities, and 4 to 6 the red, green and blue of CCIR XA/11.
    /// </summary>
    public IReadOnlyList<ushort> ChannelDesignators { get; init; } = [];

    /// <summary>Gets the number of bits of each sample.</summary>
    public int BitsPerSample { get; init; }

    /// <summary>Gets the minimum code value of each channel, or <see langword="null"/> for a channel that leaves it undefined.</summary>
    public IReadOnlyList<float?> MinimumDataValues { get; init; } = [];

    /// <summary>Gets the quantity the minimum code value of each channel represents, or <see langword="null"/>.</summary>
    public IReadOnlyList<float?> MinimumQuantities { get; init; } = [];

    /// <summary>Gets the maximum code value of each channel, or <see langword="null"/>.</summary>
    public IReadOnlyList<float?> MaximumDataValues { get; init; } = [];

    /// <summary>Gets the quantity the maximum code value of each channel represents, such as a density of 2.048, or <see langword="null"/>.</summary>
    public IReadOnlyList<float?> MaximumQuantities { get; init; } = [];

    /// <summary>
    /// Gets the CIE (x, y) chromaticities of the white point and of the red, green and blue primaries, in that order, or
    /// <see langword="null"/> when one of them is undefined. They are never applied.
    /// </summary>
    public IReadOnlyList<float>? Chromaticities { get; init; }

    /// <summary>Gets the packing field (0 for bit fields, 1 to 6 for cells of 8, 16 or 32 bits, left or right justified; the high bit packs fields of several pixels in a cell).</summary>
    public byte Packing { get; init; }

    /// <summary>Gets a value indicating whether the image is a negative (image sense 1). The pixels are not inverted.</summary>
    public bool IsNegative { get; init; }

    /// <summary>Gets the label text, or <see langword="null"/>.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the file name recorded in the header, or <see langword="null"/>.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets the creation date (<c>yyyy:mm:dd</c>, as the file stores it), or <see langword="null"/>.</summary>
    public string? CreationDate { get; init; }

    /// <summary>Gets the creation time (<c>hh:mm:ss</c> and a time zone, as the file stores it), or <see langword="null"/>.</summary>
    public string? CreationTime { get; init; }

    /// <summary>Gets the horizontal offset of the image on its source medium, or <see langword="null"/>.</summary>
    public int? XOffset { get; init; }

    /// <summary>Gets the vertical offset of the image on its source medium, or <see langword="null"/>.</summary>
    public int? YOffset { get; init; }

    /// <summary>Gets the file name of the source image, or <see langword="null"/>.</summary>
    public string? SourceFileName { get; init; }

    /// <summary>Gets the creation date of the source image, as the file stores it, or <see langword="null"/>.</summary>
    public string? SourceCreationDate { get; init; }

    /// <summary>Gets the creation time of the source image, as the file stores it, or <see langword="null"/>.</summary>
    public string? SourceCreationTime { get; init; }

    /// <summary>Gets the input device, or <see langword="null"/>.</summary>
    public string? InputDevice { get; init; }

    /// <summary>Gets the model number of the input device, or <see langword="null"/>.</summary>
    public string? InputDeviceModel { get; init; }

    /// <summary>Gets the serial number of the input device, or <see langword="null"/>.</summary>
    public string? InputDeviceSerialNumber { get; init; }

    /// <summary>Gets the horizontal pitch of the input device, in samples per millimeter, or <see langword="null"/>.</summary>
    public float? XDevicePitch { get; init; }

    /// <summary>Gets the vertical pitch of the input device, in samples per millimeter, or <see langword="null"/>.</summary>
    public float? YDevicePitch { get; init; }

    /// <summary>Gets the image gamma of the capture device, or <see langword="null"/>. It is never applied.</summary>
    public float? Gamma { get; init; }

    /// <summary>
    /// Gets the film edge code (KEYKODE): the manufacturer identification, the film type, the offset in perforations (two
    /// digits each), the prefix (six digits) and the count (four digits), or <see langword="null"/> when one of them is
    /// undefined.
    /// </summary>
    public string? FilmEdgeCode { get; init; }

    /// <summary>Gets the film format, such as <c>ACADEMY</c>, or <see langword="null"/>.</summary>
    public string? FilmFormat { get; init; }

    /// <summary>Gets the position of the frame in its sequence, or <see langword="null"/>.</summary>
    public uint? FramePosition { get; init; }

    /// <summary>Gets the frame rate of the original, in frames per second, or <see langword="null"/>.</summary>
    public float? FrameRate { get; init; }

    /// <summary>Gets the frame attribute (such as <c>KEYFRAME</c>), or <see langword="null"/>.</summary>
    public string? FrameAttribute { get; init; }

    /// <summary>Gets the slate information, or <see langword="null"/>.</summary>
    public string? SlateInformation { get; init; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] CineonHeader? other)
        => other is not null
            && string.Equals(Version, other.Version, StringComparison.Ordinal)
            && IsBigEndian == other.IsBigEndian
            && ChannelDesignators.SequenceEqual(other.ChannelDesignators)
            && BitsPerSample == other.BitsPerSample
            && MinimumDataValues.SequenceEqual(other.MinimumDataValues)
            && MinimumQuantities.SequenceEqual(other.MinimumQuantities)
            && MaximumDataValues.SequenceEqual(other.MaximumDataValues)
            && MaximumQuantities.SequenceEqual(other.MaximumQuantities)
            && (Chromaticities is null ? other.Chromaticities is null : other.Chromaticities is not null && Chromaticities.SequenceEqual(other.Chromaticities))
            && Packing == other.Packing
            && IsNegative == other.IsNegative
            && string.Equals(Label, other.Label, StringComparison.Ordinal)
            && string.Equals(FileName, other.FileName, StringComparison.Ordinal)
            && string.Equals(CreationDate, other.CreationDate, StringComparison.Ordinal)
            && string.Equals(CreationTime, other.CreationTime, StringComparison.Ordinal)
            && XOffset == other.XOffset
            && YOffset == other.YOffset
            && string.Equals(SourceFileName, other.SourceFileName, StringComparison.Ordinal)
            && string.Equals(SourceCreationDate, other.SourceCreationDate, StringComparison.Ordinal)
            && string.Equals(SourceCreationTime, other.SourceCreationTime, StringComparison.Ordinal)
            && string.Equals(InputDevice, other.InputDevice, StringComparison.Ordinal)
            && string.Equals(InputDeviceModel, other.InputDeviceModel, StringComparison.Ordinal)
            && string.Equals(InputDeviceSerialNumber, other.InputDeviceSerialNumber, StringComparison.Ordinal)
            && Nullable.Equals(XDevicePitch, other.XDevicePitch)
            && Nullable.Equals(YDevicePitch, other.YDevicePitch)
            && Nullable.Equals(Gamma, other.Gamma)
            && string.Equals(FilmEdgeCode, other.FilmEdgeCode, StringComparison.Ordinal)
            && string.Equals(FilmFormat, other.FilmFormat, StringComparison.Ordinal)
            && FramePosition == other.FramePosition
            && Nullable.Equals(FrameRate, other.FrameRate)
            && string.Equals(FrameAttribute, other.FrameAttribute, StringComparison.Ordinal)
            && string.Equals(SlateInformation, other.SlateInformation, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as CineonHeader);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Version, BitsPerSample, ChannelDesignators.Count, Packing, FileName, FramePosition);

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"Cineon {Version} {ChannelDesignators.Count} channels, {BitsPerSample}-bit");
}
