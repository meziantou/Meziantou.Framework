namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The fields of the headers of a DPX file (SMPTE ST 268) that describe its first image element and where it comes from.
/// This is informational only, like <see cref="ImageMetadata.SourceFormat"/>: it describes the file, not the pixels.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is applied to the pixels: they are the code values the file stores, scaled to the sample range of the pixel
/// format. <see cref="Transfer"/> says how they encode light (a logarithmic film scan, linear light, a video signal); a
/// conversion to linear light is an explicit operation of the caller.
/// </para>
/// <para>
/// A field the file leaves undefined (the "undefined" value of its type, all bits set for numbers and an empty string for
/// text) is <see langword="null"/>. No operation updates it, and every encoder ignores it:
/// <see cref="Formats.DpxEncoder"/> writes the fields it derives from the image itself.
/// </para>
/// </remarks>
public sealed class DpxHeader : IEquatable<DpxHeader>
{
    /// <summary>Gets the version of the header format, such as <c>V2.0</c> or <c>V1.0</c>.</summary>
    public string Version { get; init; } = "";

    /// <summary>Gets a value indicating whether the file is big-endian (magic number <c>SDPX</c>) rather than little-endian (<c>XPDS</c>).</summary>
    public bool IsBigEndian { get; init; }

    /// <summary>Gets the number of image elements of the file (1 to 8). Only the first one is decoded.</summary>
    public int ImageElementCount { get; init; }

    /// <summary>Gets the components of the first image element.</summary>
    public DpxDescriptor Descriptor { get; init; }

    /// <summary>Gets the number of bits of each component: 1, 8, 10, 12 or 16 (integers), 32 or 64 (floating point).</summary>
    public int BitDepth { get; init; }

    /// <summary>Gets how the components are packed into 32-bit words.</summary>
    public DpxPacking Packing { get; init; }

    /// <summary>Gets the transfer characteristic of the code values. It is never applied.</summary>
    public DpxTransferCharacteristic Transfer { get; init; }

    /// <summary>Gets the colorimetric specification (the primaries or the color responses) of the code values.</summary>
    public DpxColorimetricSpecification Colorimetric { get; init; }

    /// <summary>Gets the code value of the reference low quantity (the minimum expected code value), or <see langword="null"/>.</summary>
    public uint? ReferenceLowCode { get; init; }

    /// <summary>Gets the signal level or measured value the reference low code value represents, or <see langword="null"/>.</summary>
    public float? ReferenceLowQuantity { get; init; }

    /// <summary>Gets the code value of the reference high quantity (the maximum expected code value), or <see langword="null"/>.</summary>
    public uint? ReferenceHighCode { get; init; }

    /// <summary>Gets the signal level or measured value the reference high code value represents, such as a density of 2.048, or <see langword="null"/>.</summary>
    public float? ReferenceHighQuantity { get; init; }

    /// <summary>Gets the description of the first image element, or <see langword="null"/>.</summary>
    public string? ElementDescription { get; init; }

    /// <summary>Gets the file name recorded in the header, or <see langword="null"/>.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets the creation date and time (<c>yyyy:mm:dd:hh:mm:ssLTZ</c>, as the file stores it), or <see langword="null"/>.</summary>
    public string? CreationTime { get; init; }

    /// <summary>Gets the creator (the program that wrote the file), or <see langword="null"/>.</summary>
    public string? Creator { get; init; }

    /// <summary>Gets the project name, or <see langword="null"/>.</summary>
    public string? Project { get; init; }

    /// <summary>Gets the right-to-use or copyright statement, or <see langword="null"/>.</summary>
    public string? Copyright { get; init; }

    /// <summary>Gets the file name of the source image, or <see langword="null"/>.</summary>
    public string? SourceFileName { get; init; }

    /// <summary>Gets the creation date and time of the source image, as the file stores it, or <see langword="null"/>.</summary>
    public string? SourceCreationTime { get; init; }

    /// <summary>Gets the name of the input device, or <see langword="null"/>.</summary>
    public string? InputDevice { get; init; }

    /// <summary>Gets the serial number of the input device, or <see langword="null"/>.</summary>
    public string? InputDeviceSerialNumber { get; init; }

    /// <summary>
    /// Gets the film edge code (SMPTE ST 254): the manufacturer identification, the film type, the offset in perforations,
    /// the prefix and the count, 16 digits as the file stores them, or <see langword="null"/> without a motion-picture film
    /// header or when one of its parts is undefined.
    /// </summary>
    public string? FilmEdgeCode { get; init; }

    /// <summary>Gets the film format, such as <c>Academy</c>, or <see langword="null"/>.</summary>
    public string? FilmFormat { get; init; }

    /// <summary>Gets the position of the frame in its sequence, or <see langword="null"/>.</summary>
    public uint? FramePosition { get; init; }

    /// <summary>Gets the number of frames of the sequence, or <see langword="null"/>.</summary>
    public uint? SequenceLength { get; init; }

    /// <summary>Gets the number of frames for which to hold this frame, or <see langword="null"/>.</summary>
    public uint? HeldCount { get; init; }

    /// <summary>Gets the frame rate of the original film, in frames per second, or <see langword="null"/>.</summary>
    public float? FrameRate { get; init; }

    /// <summary>Gets the shutter angle of the camera, in degrees, or <see langword="null"/>.</summary>
    public float? ShutterAngle { get; init; }

    /// <summary>Gets the frame identification (such as a key frame), or <see langword="null"/>.</summary>
    public string? FrameIdentification { get; init; }

    /// <summary>Gets the slate information, or <see langword="null"/>.</summary>
    public string? SlateInformation { get; init; }

    /// <summary>
    /// Gets the SMPTE time code of the television header (SMPTE ST 12-1, Table 6) as <c>hh:mm:ss:ff</c>, or
    /// <see langword="null"/> without a television header or when it is undefined or its digits are not decimal.
    /// </summary>
    public string? TimeCode { get; init; }

    /// <summary>Gets the SMPTE user bits of the television header, or <see langword="null"/>.</summary>
    public uint? UserBits { get; init; }

    /// <summary>Gets a value indicating whether the signal is 2:1 interlaced, or <see langword="null"/>.</summary>
    public bool? IsInterlaced { get; init; }

    /// <summary>Gets the field number of the first field, or <see langword="null"/>.</summary>
    public byte? FieldNumber { get; init; }

    /// <summary>Gets the video signal standard (SMPTE ST 268, Table 4), or <see langword="null"/>.</summary>
    public byte? VideoSignalStandard { get; init; }

    /// <summary>Gets the temporal sampling rate (the frame rate) of the television header, in hertz, or <see langword="null"/>.</summary>
    public float? TemporalSamplingRate { get; init; }

    /// <summary>Gets the gamma of the television header (the exponent of <c>Y = X^(1/gamma)</c>), or <see langword="null"/>. It is never applied.</summary>
    public float? Gamma { get; init; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] DpxHeader? other)
        => other is not null
            && string.Equals(Version, other.Version, StringComparison.Ordinal)
            && IsBigEndian == other.IsBigEndian
            && ImageElementCount == other.ImageElementCount
            && Descriptor == other.Descriptor
            && BitDepth == other.BitDepth
            && Packing == other.Packing
            && Transfer == other.Transfer
            && Colorimetric == other.Colorimetric
            && ReferenceLowCode == other.ReferenceLowCode
            && Nullable.Equals(ReferenceLowQuantity, other.ReferenceLowQuantity)
            && ReferenceHighCode == other.ReferenceHighCode
            && Nullable.Equals(ReferenceHighQuantity, other.ReferenceHighQuantity)
            && string.Equals(ElementDescription, other.ElementDescription, StringComparison.Ordinal)
            && string.Equals(FileName, other.FileName, StringComparison.Ordinal)
            && string.Equals(CreationTime, other.CreationTime, StringComparison.Ordinal)
            && string.Equals(Creator, other.Creator, StringComparison.Ordinal)
            && string.Equals(Project, other.Project, StringComparison.Ordinal)
            && string.Equals(Copyright, other.Copyright, StringComparison.Ordinal)
            && string.Equals(SourceFileName, other.SourceFileName, StringComparison.Ordinal)
            && string.Equals(SourceCreationTime, other.SourceCreationTime, StringComparison.Ordinal)
            && string.Equals(InputDevice, other.InputDevice, StringComparison.Ordinal)
            && string.Equals(InputDeviceSerialNumber, other.InputDeviceSerialNumber, StringComparison.Ordinal)
            && string.Equals(FilmEdgeCode, other.FilmEdgeCode, StringComparison.Ordinal)
            && string.Equals(FilmFormat, other.FilmFormat, StringComparison.Ordinal)
            && FramePosition == other.FramePosition
            && SequenceLength == other.SequenceLength
            && HeldCount == other.HeldCount
            && Nullable.Equals(FrameRate, other.FrameRate)
            && Nullable.Equals(ShutterAngle, other.ShutterAngle)
            && string.Equals(FrameIdentification, other.FrameIdentification, StringComparison.Ordinal)
            && string.Equals(SlateInformation, other.SlateInformation, StringComparison.Ordinal)
            && string.Equals(TimeCode, other.TimeCode, StringComparison.Ordinal)
            && UserBits == other.UserBits
            && IsInterlaced == other.IsInterlaced
            && FieldNumber == other.FieldNumber
            && VideoSignalStandard == other.VideoSignalStandard
            && Nullable.Equals(TemporalSamplingRate, other.TemporalSamplingRate)
            && Nullable.Equals(Gamma, other.Gamma);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as DpxHeader);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Version, Descriptor, BitDepth, Transfer, Colorimetric, Creator, TimeCode, FramePosition);

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"DPX {Version} {Descriptor} {BitDepth}-bit, transfer {Transfer}, colorimetric {Colorimetric}");
}
