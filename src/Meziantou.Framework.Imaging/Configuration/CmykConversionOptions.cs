using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the color conversion that loads images whose samples are CMYK inks (four-component JPEG files
/// and separated TIFF pages):
/// see <see cref="ImageDecodeOptions.CmykConversion"/> and <see cref="ImageReaderOptions.CmykConversion"/>.
/// </summary>
/// <remarks>
/// <para>
/// No pixel format holds CMYK samples: the decoder converts them with an <see cref="IccColorTransform"/> from the CMYK
/// profile embedded in the file, or from <see cref="AssumedSourceProfile"/> when the file has none, to
/// <see cref="DestinationProfile"/>. The loaded image holds RGB pixels labeled with <see cref="DestinationProfile"/>;
/// the CMYK profile is not kept. An alpha sample of a TIFF page is not a color: it is copied unchanged.
/// </para>
/// <para>
/// An embedded CMYK profile that is malformed or not supported is an error: it is never replaced by
/// <see cref="AssumedSourceProfile"/>.
/// </para>
/// </remarks>
public sealed class CmykConversionOptions
{
    private IccColorTransform? _assumedTransform;

    /// <summary>
    /// Gets the default options: conversion to sRGB, <see cref="IccProfile.Cgats001CompatibleCmyk"/> assumed for samples
    /// without profile, media-relative colorimetric intent with black point compensation.
    /// </summary>
    public static CmykConversionOptions Default { get; } = new();

    /// <summary>Gets the RGB profile of the loaded pixels. Defaults to <see cref="IccProfile.Srgb"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The profile does not declare the RGB color space.</exception>
    public IccProfile DestinationProfile
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.ColorSpace != IccProfileColorSpace.Rgb)
                throw new ArgumentException($"The destination profile must declare the RGB color space, but it declares {value.ColorSpace}.", nameof(value));

            field = value;
        }
    } = IccProfile.Srgb;

    /// <summary>
    /// Gets the CMYK profile assumed for samples without embedded CMYK profile, or <see langword="null"/> to reject such
    /// images with an <see cref="UnsupportedImageFeatureException"/>. Defaults to
    /// <see cref="IccProfile.Cgats001CompatibleCmyk"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The profile does not declare the CMYK color space.</exception>
    public IccProfile? AssumedSourceProfile
    {
        get;
        init
        {
            if (value is not null && value.ColorSpace != IccProfileColorSpace.Cmyk)
                throw new ArgumentException($"The assumed source profile must declare the CMYK color space, but it declares {value.ColorSpace}.", nameof(value));

            field = value;
        }
    } = IccProfile.Cgats001CompatibleCmyk;

    /// <summary>Gets the rendering intent and the black point compensation of the conversion. Defaults to <see cref="IccColorTransformOptions.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public IccColorTransformOptions Transform
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = IccColorTransformOptions.Default;

    /// <summary>Creates the conversion from a CMYK profile to <see cref="DestinationProfile"/>.</summary>
    /// <param name="embeddedProfile">The CMYK profile of the image, or <see langword="null"/> to use <see cref="AssumedSourceProfile"/>.</param>
    /// <returns>The conversion, or <see langword="null"/> when the image has no profile and none is assumed.</returns>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A profile is valid but not supported, or the destination cannot be one.</exception>
    internal IccColorTransform? CreateTransform(IccProfile? embeddedProfile)
    {
        if (embeddedProfile is not null)
            return IccColorTransform.Create(embeddedProfile, DestinationProfile, Transform);

        if (AssumedSourceProfile is null)
            return null;

        // The options are immutable and a transform is thread-safe: the conversion from the assumed profile is built once
        return _assumedTransform ??= IccColorTransform.Create(AssumedSourceProfile, DestinationProfile, Transform);
    }
}
