namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// An immutable content light level (CTA-861.3, the <c>clli</c> box of ISO/IEC 14496-12): the upper bounds of the light
/// level of high dynamic range content, in candelas per square meter.
/// </summary>
/// <remarks>
/// The values are stored as the file declares them and describe the pixels as they were mastered: no operation computes
/// them from the pixels. A value of 0 means that the bound is unknown. Every value is accepted, so that a description
/// round-trips unchanged.
/// </remarks>
public sealed class ContentLightLevel : IEquatable<ContentLightLevel>
{
    /// <summary>Initializes a new instance of the <see cref="ContentLightLevel"/> class.</summary>
    /// <param name="maxContentLightLevel">The light level of the brightest pixel of the content, in candelas per square meter, or 0 when unknown.</param>
    /// <param name="maxPictureAverageLightLevel">The largest average light level of a picture of the content, in candelas per square meter, or 0 when unknown.</param>
    public ContentLightLevel(ushort maxContentLightLevel, ushort maxPictureAverageLightLevel)
    {
        MaxContentLightLevel = maxContentLightLevel;
        MaxPictureAverageLightLevel = maxPictureAverageLightLevel;
    }

    /// <summary>Gets the light level of the brightest pixel of the content (MaxCLL), in candelas per square meter, or 0 when unknown.</summary>
    public ushort MaxContentLightLevel { get; }

    /// <summary>Gets the largest average light level of a picture of the content (MaxPALL or MaxFALL), in candelas per square meter, or 0 when unknown.</summary>
    public ushort MaxPictureAverageLightLevel { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] ContentLightLevel? other) => other is not null && MaxContentLightLevel == other.MaxContentLightLevel && MaxPictureAverageLightLevel == other.MaxPictureAverageLightLevel;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ContentLightLevel);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MaxContentLightLevel, MaxPictureAverageLightLevel);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"MaxCLL {MaxContentLightLevel} cd/m2, MaxPALL {MaxPictureAverageLightLevel} cd/m2");
}
