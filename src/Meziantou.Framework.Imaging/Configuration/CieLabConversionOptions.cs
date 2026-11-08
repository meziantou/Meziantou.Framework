using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the color conversion that loads images whose samples are CIE 1976 L*a*b* colors (TIFF pages
/// with the <c>CIELab</c> and <c>ICCLab</c> photometric interpretations): see <see cref="ImageDecodeOptions.CieLabConversion"/>
/// and <see cref="ImageReaderOptions.CieLabConversion"/>.
/// </summary>
/// <remarks>
/// <para>
/// No pixel format holds L*a*b* samples: the decoder converts each color, relative to the white point of the file
/// (D50 when the file declares none, as Adobe's TIFF technical note says), to CIEXYZ of the ICC profile connection space
/// (adapted to D50 with the linear Bradford transform), then to <see cref="DestinationProfile"/>. The loaded image holds
/// RGB pixels labeled with <see cref="DestinationProfile"/>. The colors outside its gamut are brought into it as
/// <see cref="IccColorTransformOptions.GamutMapping"/> says, so the conversion can lose colors. An alpha sample is not a
/// color: it is copied unchanged.
/// </para>
/// <para>
/// The colors are treated as the colorimetry of the connection space: their white is the white of the connection space,
/// so the ICC-absolute colorimetric intent rescales them by the media white of the destination only, and their black is
/// no light (L* = 0), which is what black point compensation maps to the black of the destination.
/// </para>
/// </remarks>
public sealed class CieLabConversionOptions
{
    /// <summary>Gets the default options: conversion to sRGB with the media-relative colorimetric intent and black point compensation.</summary>
    public static CieLabConversionOptions Default { get; } = new();

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

    /// <summary>Gets the rendering intent, the black point compensation and the gamut mapping of the conversion. Defaults to <see cref="IccColorTransformOptions.Default"/>.</summary>
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

    /// <summary>Creates the conversion of encoded L*a*b* samples to <see cref="DestinationProfile"/>.</summary>
    /// <param name="decode">The stage that turns the normalized samples (0 to 1) into L* (0 to 100), a* and b*.</param>
    /// <param name="adaptation">
    /// The stage that turns CIEXYZ relative to the D50 illuminant of the connection space for the same L*a*b* values into
    /// the colors relative to the connection space of a file with another white point, or <see langword="null"/> for D50.
    /// </param>
    /// <returns>The conversion, from three samples to three samples.</returns>
    /// <exception cref="InvalidImageContentException">The destination profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The destination profile cannot be a destination.</exception>
    internal IccPipeline CreatePipeline(IccMatrixStage decode, IccMatrixStage? adaptation)
        => IccPipeline.CreateFromCieLab(decode, adaptation, DestinationProfile, Transform.Intent, Transform.BlackPointCompensation, Transform.GamutMapping);
}
