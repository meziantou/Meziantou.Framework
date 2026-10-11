using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The rules deciding whether an ICC profile can label pixels of a given format. Layout
/// conversion is not color management: a profile is never applied implicitly, so it can only be kept when its declared
/// color space matches the pixels: <see cref="IccProfileColorSpace.Gray"/> for gray formats, <see cref="IccProfileColorSpace.Rgb"/> for
/// color formats. Profiles declaring any other (or no) color space are compatible with no built-in pixel format.
/// </summary>
internal static class ColorProfileCompatibility
{
    /// <summary>Determines whether a profile color space can label pixels of a format.</summary>
    /// <param name="colorSpace">The declared color space.</param>
    /// <param name="format">The pixel format.</param>
    /// <returns><see langword="true"/> if the combination is compatible.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool IsCompatible(IccProfileColorSpace colorSpace, PixelFormat format)
        => PixelFormats.IsGrayscale(format) ? colorSpace == IccProfileColorSpace.Gray : colorSpace == IccProfileColorSpace.Rgb;

    /// <summary>Determines whether a profile can label pixels of a format (no profile is always compatible).</summary>
    /// <param name="profile">The profile, or <see langword="null"/>.</param>
    /// <param name="format">The pixel format.</param>
    /// <returns><see langword="true"/> if the combination is compatible.</returns>
    public static bool IsCompatible(IccProfile? profile, PixelFormat format)
        => profile is null || IsCompatible(profile.ColorSpace, format);

    /// <summary>
    /// Gets the profile to retain on pixels converted to <paramref name="destinationFormat"/>: the profile itself when it is
    /// compatible, <see langword="null"/> when it is incompatible and <paramref name="discardIncompatible"/> is set.
    /// The resulting combination is always checked, so an incompatible profile that the caller attached to the source is
    /// reported too.
    /// </summary>
    /// <param name="profile">The retained profile of the source, or <see langword="null"/>.</param>
    /// <param name="destinationFormat">The converted pixel format.</param>
    /// <param name="discardIncompatible">Whether an incompatible profile is removed instead of causing an error.</param>
    /// <param name="format">The image format involved (for the exception), or <see cref="ImageFormat.Unknown"/>.</param>
    /// <returns>The profile to retain, or <see langword="null"/>.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The profile is incompatible and <paramref name="discardIncompatible"/> is <see langword="false"/>.</exception>
    public static IccProfile? Resolve(IccProfile? profile, PixelFormat destinationFormat, bool discardIncompatible, ImageFormat format = ImageFormat.Unknown)
    {
        if (profile is null || IsCompatible(profile.ColorSpace, destinationFormat))
            return profile;

        if (discardIncompatible)
            return null;

        var kind = PixelFormats.IsGrayscale(destinationFormat) ? "grayscale" : "color";
        throw new UnsupportedImageFeatureException(
            $"The ICC profile declares the {profile.ColorSpace} color space, which cannot label {kind} {destinationFormat} pixels. Layout conversion does not apply color profiles; convert the colors first with ConvertColorProfile, set PixelConversionOptions.DiscardIncompatibleColorProfile to remove the profile, or remove it from the metadata.",
            format,
            "Incompatible color profile");
    }
}
