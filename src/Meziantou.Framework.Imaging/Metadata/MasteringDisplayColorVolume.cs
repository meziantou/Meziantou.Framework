namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// An immutable mastering display color volume (SMPTE ST 2086, the <c>mdcv</c> box of ISO/IEC 14496-12): the color
/// primaries, the white point and the luminance range of the display on which high dynamic range content was mastered.
/// </summary>
/// <remarks>
/// <para>
/// The values are kept as the integers the file stores, so that a description round-trips unchanged: chromaticity
/// coordinates in units of 0.00002 (50,000 is 1.0) and luminances in units of 0.0001 candela per square meter. Every
/// value is accepted, the ones a display cannot have included.
/// </para>
/// <para>
/// This is a label: it describes the display the content was graded on, not the color space of the pixels
/// (<see cref="ImageMetadata.Cicp"/> or <see cref="ImageMetadata.IccProfile"/> does), and no operation computes it.
/// </para>
/// </remarks>
public sealed class MasteringDisplayColorVolume : IEquatable<MasteringDisplayColorVolume>
{
    private const double ChromaticityUnit = 0.00002;
    private const double LuminanceUnit = 0.0001;

    /// <summary>Initializes a new instance of the <see cref="MasteringDisplayColorVolume"/> class.</summary>
    /// <param name="redX">The x chromaticity coordinate of the red primary, in units of 0.00002.</param>
    /// <param name="redY">The y chromaticity coordinate of the red primary, in units of 0.00002.</param>
    /// <param name="greenX">The x chromaticity coordinate of the green primary, in units of 0.00002.</param>
    /// <param name="greenY">The y chromaticity coordinate of the green primary, in units of 0.00002.</param>
    /// <param name="blueX">The x chromaticity coordinate of the blue primary, in units of 0.00002.</param>
    /// <param name="blueY">The y chromaticity coordinate of the blue primary, in units of 0.00002.</param>
    /// <param name="whitePointX">The x chromaticity coordinate of the white point, in units of 0.00002.</param>
    /// <param name="whitePointY">The y chromaticity coordinate of the white point, in units of 0.00002.</param>
    /// <param name="maxLuminance">The nominal maximum luminance of the display, in units of 0.0001 candela per square meter.</param>
    /// <param name="minLuminance">The nominal minimum luminance of the display, in units of 0.0001 candela per square meter.</param>
    public MasteringDisplayColorVolume(ushort redX, ushort redY, ushort greenX, ushort greenY, ushort blueX, ushort blueY, ushort whitePointX, ushort whitePointY, uint maxLuminance, uint minLuminance)
    {
        RedX = redX;
        RedY = redY;
        GreenX = greenX;
        GreenY = greenY;
        BlueX = blueX;
        BlueY = blueY;
        WhitePointX = whitePointX;
        WhitePointY = whitePointY;
        MaxLuminance = maxLuminance;
        MinLuminance = minLuminance;
    }

    /// <summary>Gets the x chromaticity coordinate of the red primary, in units of 0.00002.</summary>
    public ushort RedX { get; }

    /// <summary>Gets the y chromaticity coordinate of the red primary, in units of 0.00002.</summary>
    public ushort RedY { get; }

    /// <summary>Gets the x chromaticity coordinate of the green primary, in units of 0.00002.</summary>
    public ushort GreenX { get; }

    /// <summary>Gets the y chromaticity coordinate of the green primary, in units of 0.00002.</summary>
    public ushort GreenY { get; }

    /// <summary>Gets the x chromaticity coordinate of the blue primary, in units of 0.00002.</summary>
    public ushort BlueX { get; }

    /// <summary>Gets the y chromaticity coordinate of the blue primary, in units of 0.00002.</summary>
    public ushort BlueY { get; }

    /// <summary>Gets the x chromaticity coordinate of the white point, in units of 0.00002.</summary>
    public ushort WhitePointX { get; }

    /// <summary>Gets the y chromaticity coordinate of the white point, in units of 0.00002.</summary>
    public ushort WhitePointY { get; }

    /// <summary>Gets the nominal maximum luminance of the display, in units of 0.0001 candela per square meter.</summary>
    public uint MaxLuminance { get; }

    /// <summary>Gets the nominal minimum luminance of the display, in units of 0.0001 candela per square meter.</summary>
    public uint MinLuminance { get; }

    /// <summary>Gets the nominal maximum luminance of the display, in candelas per square meter.</summary>
    public double MaxLuminanceCandelasPerSquareMeter => MaxLuminance * LuminanceUnit;

    /// <summary>Gets the nominal minimum luminance of the display, in candelas per square meter.</summary>
    public double MinLuminanceCandelasPerSquareMeter => MinLuminance * LuminanceUnit;

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] MasteringDisplayColorVolume? other)
    {
        return other is not null
            && RedX == other.RedX
            && RedY == other.RedY
            && GreenX == other.GreenX
            && GreenY == other.GreenY
            && BlueX == other.BlueX
            && BlueY == other.BlueY
            && WhitePointX == other.WhitePointX
            && WhitePointY == other.WhitePointY
            && MaxLuminance == other.MaxLuminance
            && MinLuminance == other.MinLuminance;
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as MasteringDisplayColorVolume);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(RedX);
        hash.Add(RedY);
        hash.Add(GreenX);
        hash.Add(GreenY);
        hash.Add(BlueX);
        hash.Add(BlueY);
        hash.Add(WhitePointX);
        hash.Add(WhitePointY);
        hash.Add(MaxLuminance);
        hash.Add(MinLuminance);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"R({RedX * ChromaticityUnit}, {RedY * ChromaticityUnit}) G({GreenX * ChromaticityUnit}, {GreenY * ChromaticityUnit}) B({BlueX * ChromaticityUnit}, {BlueY * ChromaticityUnit}) W({WhitePointX * ChromaticityUnit}, {WhitePointY * ChromaticityUnit}), {MinLuminanceCandelasPerSquareMeter} to {MaxLuminanceCandelasPerSquareMeter} cd/m2");
    }
}
