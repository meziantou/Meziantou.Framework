using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The meaning of red, green and blue values: how the color value types (<see cref="CieXyz"/>, <see cref="CieLab"/>,
/// <see cref="Oklab"/>, ...) read an <see cref="Rgba32"/> or <see cref="Rgba64"/> color and write one. Instances are
/// immutable and thread-safe: create one once and reuse it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Srgb"/>, the default of every conversion, follows the sample code of CSS Color Module Level 4: the sRGB
/// transfer function, the rational sRGB matrix to CIEXYZ relative to D65, and the linear Bradford adaptation to D50 that
/// CSS publishes. A space created from an ICC profile converts with the profile (the relative colorimetric intent), to the
/// CIEXYZ of the profile connection space, whose white (D50 as ICC.1 rounds it: 0.9642, 1, 0.8249) is then adapted to
/// <see cref="CieXyz.D50"/> with the linear Bradford transform. A space created from a CICP color description converts
/// with the functions of Rec. ITU-T H.273 and the matrix of its primaries, adapted to D50 with the linear Bradford
/// transform.
/// </para>
/// <para>
/// Alpha is not a color coordinate: conversions from RGB ignore it, and conversions to RGB give opaque colors. A color
/// outside the gamut of the space is clipped component by component, after the transfer function.
/// </para>
/// </remarks>
public sealed class RgbColorSpace
{
    // The white of the ICC profile connection space (ICC.1:2022 section 6.3.4.3)
    private static readonly ColorValueMatrix ConnectionSpaceToD50 = ColorValueMath.GetBradfordAdaptation(IccColorimetry.D50X, IccColorimetry.D50Y, IccColorimetry.D50Z, ColorValueMath.D50X, 1, ColorValueMath.D50Z);
    private static readonly ColorValueMatrix D50ToConnectionSpace = ColorValueMath.GetBradfordAdaptation(ColorValueMath.D50X, 1, ColorValueMath.D50Z, IccColorimetry.D50X, IccColorimetry.D50Y, IccColorimetry.D50Z);

    private readonly SpaceKind _kind;

    // CICP: the transfer function, and the matrices from linear values to CIEXYZ relative to D50 and to D65
    private readonly byte _transfer;
    private readonly ColorValueMatrix _toD50;
    private readonly ColorValueMatrix _fromD50;
    private readonly ColorValueMatrix _toD65;
    private readonly ColorValueMatrix _fromD65;

    // ICC: the two halves of a conversion through the profile connection space
    private readonly IccPipeline? _toConnectionSpace;
    private readonly IccPipeline? _fromConnectionSpace;

    private RgbColorSpace()
    {
        _kind = SpaceKind.Srgb;
        Description = "sRGB";
    }

    private RgbColorSpace(byte transfer, ColorValueMatrix toD50, string description)
    {
        _kind = SpaceKind.Cicp;
        _transfer = transfer;
        _toD50 = toD50;
        _fromD50 = toD50.Invert();
        _toD65 = ColorValueMath.D50ToD65.Multiply(toD50);
        _fromD65 = _toD65.Invert();
        Description = description;
    }

    private RgbColorSpace(IccPipeline toConnectionSpace, IccPipeline? fromConnectionSpace, string description)
    {
        _kind = SpaceKind.Icc;
        _toConnectionSpace = toConnectionSpace;
        _fromConnectionSpace = fromConnectionSpace;
        Description = description;
    }

    private enum SpaceKind
    {
        Srgb,
        Cicp,
        Icc,
    }

    /// <summary>Gets the sRGB color space of IEC 61966-2-1, as CSS Color 4 converts it. It is the default of every conversion.</summary>
    public static RgbColorSpace Srgb { get; } = new();

    /// <summary>Gets a short description of the space, for diagnostics.</summary>
    public string Description { get; }

    /// <summary>Creates the color space an RGB ICC profile describes.</summary>
    /// <param name="profile">An RGB profile.</param>
    /// <returns>The color space.</returns>
    /// <remarks>
    /// The conversions use the relative colorimetric intent of the profile. A profile that cannot convert colors to its
    /// RGB values (an input profile with lookup tables only, a constant tone curve) gives a space that reads colors but
    /// cannot write them: <see cref="CanConvertFromXyz"/> is <see langword="false"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile is valid but does not describe RGB colors, or is not supported.</exception>
    public static RgbColorSpace FromIccProfile(IccProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ColorSpace != IccProfileColorSpace.Rgb)
            throw new UnsupportedImageFeatureException($"The ICC profile describes {profile.ColorSpace} colors, not RGB colors.", ImageFormat.Unknown, "Color values of a non-RGB ICC profile");

        var toConnectionSpace = IccPipeline.CreateToConnectionSpace(profile, IccRenderingIntent.RelativeColorimetric);
        IccPipeline? fromConnectionSpace;
        try
        {
            fromConnectionSpace = IccPipeline.TryCreateFromConnectionSpace(profile, IccRenderingIntent.RelativeColorimetric);
        }
        catch (UnsupportedImageFeatureException)
        {
            // A valid profile that cannot be a destination (a constant tone curve, colorants that do not span a color
            // space) still reads colors
            fromConnectionSpace = null;
        }

        return new RgbColorSpace(toConnectionSpace, fromConnectionSpace, "ICC profile");
    }

    /// <summary>Creates the color space a CICP color description (Rec. ITU-T H.273) gives to RGB samples.</summary>
    /// <param name="description">The description. Its matrix coefficients and its range describe how a file stored the samples and play no part.</param>
    /// <returns>The color space; <see cref="Srgb"/> for the sRGB primaries with the sRGB transfer characteristics.</returns>
    /// <remarks>
    /// Unspecified primaries (2) are those of sRGB and unspecified transfer characteristics (2) are the sRGB function, as
    /// for an image. The high dynamic range functions (PQ and HLG) are not supported: their light exceeds the white of the
    /// color values, so their colors need tone mapping, which <see cref="ImageProcessingExtensions.ConvertColorProfile(Image, IccProfile, IccColorTransformOptions?, CancellationToken)"/> does.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The primaries or the transfer characteristics are reserved, or the transfer characteristics are PQ or HLG.</exception>
    public static RgbColorSpace FromCicp(CicpColorDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var primaries = description.ColorPrimaries is 2 ? (byte)1 : description.ColorPrimaries;
        var transfer = description.TransferCharacteristics is 2 ? CicpTransfer.Srgb : description.TransferCharacteristics;
        if (!CicpTransfer.IsSupported(transfer))
            throw new UnsupportedImageFeatureException($"The transfer characteristics of the CICP color description ({description}) are reserved.", ImageFormat.Unknown, "Color values of reserved transfer characteristics");

        if (CicpTransfer.IsHdr(transfer))
            throw new UnsupportedImageFeatureException($"The transfer characteristics of the CICP color description ({description}) are high dynamic range ones, whose colors need tone mapping.", ImageFormat.Unknown, "Color values of high dynamic range samples");

        if (!CicpPrimaries.IsSupported(primaries))
            throw new UnsupportedImageFeatureException($"The color primaries of the CICP color description ({description}) are reserved.", ImageFormat.Unknown, "Color values of reserved color primaries");

        if (primaries is 1 && transfer is CicpTransfer.Srgb)
            return Srgb;

        // The matrix to the connection space adapts the white of the primaries to the D50 of ICC.1, then to the D50 of CSS
        Span<double> matrix = stackalloc double[9];
        if (!new CicpColorSpace(primaries, transfer, IsGray: false).TryGetToConnectionSpace(matrix))
            throw new UnsupportedImageFeatureException($"The color primaries of the CICP color description ({description}) do not define a color space.", ImageFormat.Unknown, "CICP color primaries without a color space");

        return new RgbColorSpace(transfer, ConnectionSpaceToD50.Multiply(ColorValueMatrix.FromRows(matrix)), string.Create(CultureInfo.InvariantCulture, $"CICP {primaries}/{transfer}"));
    }

    /// <summary>Gets a value indicating whether the space can convert CIEXYZ to RGB values: <see langword="false"/> only for an ICC profile that describes one direction.</summary>
    public bool CanConvertFromXyz => _kind != SpaceKind.Icc || _fromConnectionSpace is not null;

    /// <inheritdoc />
    public override string ToString() => Description;

    /// <summary>Converts encoded red, green and blue values in [0, 1] to CIEXYZ relative to <see cref="CieXyz.D50"/>.</summary>
    internal (double X, double Y, double Z) ToXyzD50(double red, double green, double blue)
    {
        switch (_kind)
        {
            case SpaceKind.Srgb:
                var (x, y, z) = ColorValueMath.LinearSrgbToXyzD65.Transform(ColorValueMath.DecodeSrgb(red), ColorValueMath.DecodeSrgb(green), ColorValueMath.DecodeSrgb(blue));
                return ColorValueMath.D65ToD50.Transform(x, y, z);

            case SpaceKind.Cicp:
                return _toD50.Transform(CicpTransfer.Decode(_transfer, red), CicpTransfer.Decode(_transfer, green), CicpTransfer.Decode(_transfer, blue));

            default:
                var (px, py, pz) = ToConnectionSpace(red, green, blue);
                return ConnectionSpaceToD50.Transform(px, py, pz);
        }
    }

    /// <summary>Converts encoded red, green and blue values in [0, 1] to CIEXYZ relative to <see cref="CieXyz.D65"/>.</summary>
    internal (double X, double Y, double Z) ToXyzD65(double red, double green, double blue)
    {
        switch (_kind)
        {
            case SpaceKind.Srgb:
                return ColorValueMath.LinearSrgbToXyzD65.Transform(ColorValueMath.DecodeSrgb(red), ColorValueMath.DecodeSrgb(green), ColorValueMath.DecodeSrgb(blue));

            case SpaceKind.Cicp:
                return _toD65.Transform(CicpTransfer.Decode(_transfer, red), CicpTransfer.Decode(_transfer, green), CicpTransfer.Decode(_transfer, blue));

            default:
                var (x, y, z) = ToXyzD50(red, green, blue);
                return ColorValueMath.D50ToD65.Transform(x, y, z);
        }
    }

    /// <summary>Converts CIEXYZ relative to <see cref="CieXyz.D50"/> to encoded red, green and blue values, not clipped for sRGB.</summary>
    internal (double Red, double Green, double Blue) FromXyzD50(double x, double y, double z)
    {
        switch (_kind)
        {
            case SpaceKind.Srgb:
                var (dx, dy, dz) = ColorValueMath.D50ToD65.Transform(x, y, z);
                return FromXyzD65(dx, dy, dz);

            case SpaceKind.Cicp:
                var (r, g, b) = _fromD50.Transform(x, y, z);
                return (CicpTransfer.Encode(_transfer, r), CicpTransfer.Encode(_transfer, g), CicpTransfer.Encode(_transfer, b));

            default:
                var (px, py, pz) = D50ToConnectionSpace.Transform(x, y, z);
                return FromConnectionSpace(px, py, pz);
        }
    }

    /// <summary>Converts CIEXYZ relative to <see cref="CieXyz.D65"/> to encoded red, green and blue values, not clipped for sRGB.</summary>
    internal (double Red, double Green, double Blue) FromXyzD65(double x, double y, double z)
    {
        switch (_kind)
        {
            case SpaceKind.Srgb:
                var (r, g, b) = ColorValueMath.XyzD65ToLinearSrgb.Transform(x, y, z);
                return (ColorValueMath.EncodeSrgb(r), ColorValueMath.EncodeSrgb(g), ColorValueMath.EncodeSrgb(b));

            case SpaceKind.Cicp:
                var (lr, lg, lb) = _fromD65.Transform(x, y, z);
                return (CicpTransfer.Encode(_transfer, lr), CicpTransfer.Encode(_transfer, lg), CicpTransfer.Encode(_transfer, lb));

            default:
                var (dx, dy, dz) = ColorValueMath.D65ToD50.Transform(x, y, z);
                return FromXyzD50(dx, dy, dz);
        }
    }

    private (double X, double Y, double Z) ToConnectionSpace(double red, double green, double blue)
    {
        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        values[0] = red;
        values[1] = green;
        values[2] = blue;
        _toConnectionSpace!.Apply(values);
        return (values[0], values[1], values[2]);
    }

    private (double Red, double Green, double Blue) FromConnectionSpace(double x, double y, double z)
    {
        if (_fromConnectionSpace is null)
            throw new UnsupportedImageFeatureException("The ICC profile does not describe the conversion of colors to its RGB values.", ImageFormat.Unknown, "Color values written with an input-only ICC profile");

        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        values[0] = x;
        values[1] = y;
        values[2] = z;
        _fromConnectionSpace.Apply(values);
        return (values[0], values[1], values[2]);
    }
}
