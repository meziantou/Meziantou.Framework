using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color space an image stored in XYB is rendered to when its ICC profile is not sRGB: a matrix-based RGB profile or a
/// monochrome profile, whose CIEXYZ connection space converts to device values with the inverse of its colorants and of
/// its tone curves for every rendering intent (ICC.1:2022 sections 8.3.3 and 8.3.4). XYB describes linear sRGB; the
/// samples are adapted to the illuminant of the connection space, then converted to the device values of the profile
/// exactly as the color management of the library converts to it (<see cref="IccProfileModel.AppendFromConnectionSpace"/>).
/// </summary>
internal sealed class JpegXlIccOutput
{
    private readonly double[] _deviceFromConnectionSpace;

    private JpegXlIccOutput(double[] deviceFromConnectionSpace, IccInverseCurve[] curves)
    {
        _deviceFromConnectionSpace = deviceFromConnectionSpace;
        Curves = curves;
    }

    /// <summary>Gets the inverse of the colorant matrix, row by row (the identity for a monochrome profile).</summary>
    public ReadOnlySpan<double> DeviceFromConnectionSpace => _deviceFromConnectionSpace;

    /// <summary>Gets the inverse tone curve of each channel: one for a monochrome profile, three for an RGB one.</summary>
    public IccInverseCurve[] Curves { get; }

    /// <summary>Gets the rendering of a profile.</summary>
    /// <param name="profile">The profile of the image, compatible with its samples.</param>
    /// <returns>
    /// The rendering, or <see langword="null"/> when the profile converts from the connection space with a lookup table, has
    /// a CIELAB connection space, or cannot be a destination (singular colorants, constant curve, class that does not
    /// describe a device).
    /// </returns>
    /// <exception cref="InvalidImageContentException">A colorant or a curve of the profile is malformed.</exception>
    public static JpegXlIccOutput? TryCreate(IccProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            var model = IccProfileModel.Create(profile, "image");
            var matrix = new double[9];
            return model.TryGetMatrixShaperFromConnectionSpace(matrix, out var curves) ? new JpegXlIccOutput(matrix, curves) : null;
        }
        catch (UnsupportedImageFeatureException)
        {
            return null;
        }
        catch (InvalidImageContentException exception)
        {
            throw JpegXlFormat.Invalid("the ICC profile cannot be used: " + exception.Message);
        }
    }
}
