using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What an <see cref="Image"/> can hold of a JPEG XL image, decided once from the image header and used by identification
/// and decoding alike: the content that is rejected because loading it would lose information, the default pixel format,
/// and the color labels.
/// </summary>
internal static class JpegXlFeaturePolicy
{
    /// <summary>Rejects the images whose samples or channels an image cannot hold.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The image uses such a feature.</exception>
    public static void EnsureSupported(JpegXlImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        EnsureSupportedDepth(header.BitDepth, "color");
        if (!header.IsXybEncoded && header.ColorEncoding.ColorSpace == JpegXlColorEncoding.ColorSpaceXyb)
            throw JpegXlFormat.Unsupported("The JPEG XL image stores XYB samples that are not to be converted.", "XYB samples");

        if (header.LoopCount > int.MaxValue)
            throw JpegXlFormat.Unsupported("The JPEG XL animation is played more than 2,147,483,647 times.", "play count");

        var alphaSeen = false;
        foreach (var channel in header.ExtraChannels)
        {
            switch (channel.Type)
            {
                case JpegXlExtraChannelInfo.TypeBlack:
                    throw JpegXlFormat.Unsupported("The JPEG XL image has a black channel (CMYK).", "CMYK");

                case JpegXlExtraChannelInfo.TypeSpotColor:
                    throw JpegXlFormat.Unsupported("The JPEG XL image has a spot color channel.", "spot color");

                case JpegXlExtraChannelInfo.TypeAlpha when !alphaSeen:
                    // The first alpha channel is the transparency of the image; the others are auxiliary data
                    alphaSeen = true;
                    EnsureSupportedDepth(channel.BitDepth, "alpha");
                    if (channel.IsAlphaAssociated)
                        throw JpegXlFormat.Unsupported("The JPEG XL image has premultiplied alpha.", "premultiplied alpha");

                    break;
            }
        }
    }

    /// <summary>Gets a value indicating whether the samples of the image need 16 bits: the color channels or the alpha channel have more than 8 bits.</summary>
    public static bool IsSixteenBit(JpegXlImageHeader header)
    {
        var alpha = header.AlphaChannelIndex;
        return header.BitDepth.BitsPerSample > 8 || (alpha >= 0 && header.ExtraChannels[alpha].BitDepth.BitsPerSample > 8);
    }

    /// <summary>
    /// Gets the CICP description that labels an enumerated color encoding, or <see langword="null"/> when the encoding is
    /// sRGB (no label), linear sRGB (labelled by the transfer function of the metadata), or has no code point.
    /// </summary>
    /// <param name="encoding">The color encoding.</param>
    /// <param name="hasCodePoints">Receives <see langword="false"/> when the encoding is neither sRGB, linear sRGB nor expressible with code points.</param>
    public static CicpColorDescription? GetCicp(JpegXlColorEncoding encoding, out bool hasCodePoints)
    {
        hasCodePoints = true;
        if (encoding.IsSrgb || encoding.IsLinearSrgb)
            return null;

        hasCodePoints = false;
        if (encoding.WantIcc || encoding.HasGamma || encoding.ColorSpace is not (JpegXlColorEncoding.ColorSpaceRgb or JpegXlColorEncoding.ColorSpaceGray))
            return null;

        // The primaries of ITU-T H.273 include their white point: P3 has two code points, the others are D65
        byte primaries;
        if (encoding.IsGray)
        {
            if (encoding.WhitePoint != JpegXlColorEncoding.WhitePointD65)
                return null;

            primaries = 1;
        }
        else
        {
            switch (encoding.Primaries, encoding.WhitePoint)
            {
                case (JpegXlColorEncoding.PrimariesSrgb, JpegXlColorEncoding.WhitePointD65):
                    primaries = 1;
                    break;
                case (JpegXlColorEncoding.PrimariesRec2100, JpegXlColorEncoding.WhitePointD65):
                    primaries = 9;
                    break;
                case (JpegXlColorEncoding.PrimariesP3, JpegXlColorEncoding.WhitePointDci):
                    primaries = 11;
                    break;
                case (JpegXlColorEncoding.PrimariesP3, JpegXlColorEncoding.WhitePointD65):
                    primaries = 12;
                    break;
                default:
                    return null;
            }
        }

        // The transfer functions of JPEG XL are numbered with the code points of H.273; the samples are full-range R'G'B'
        hasCodePoints = true;
        return new CicpColorDescription(primaries, (byte)encoding.TransferFunction, matrixCoefficients: 0, fullRange: true);
    }

    /// <summary>
    /// Creates the ICC profile that describes an enumerated color encoding without code points in ITU-T H.273: custom
    /// primaries, a custom white point or a power transfer function.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The transfer function has no parametric form in an ICC profile (PQ, HLG).</exception>
    public static IccProfile CreateProfile(JpegXlColorEncoding encoding)
    {
        BuiltInIccCurve curve;
        var exponent = 1.0;
        string transfer;
        if (encoding.HasGamma)
        {
            curve = BuiltInIccCurve.Power;
            exponent = 1 / (encoding.Gamma * JpegXlColorEncoding.GammaScale);
            transfer = string.Create(CultureInfo.InvariantCulture, $"gamma {encoding.Gamma * JpegXlColorEncoding.GammaScale:0.#######}");
        }
        else
        {
            switch (encoding.TransferFunction)
            {
                case JpegXlColorEncoding.TransferSrgb:
                    (curve, transfer) = (BuiltInIccCurve.Srgb, "sRGB");
                    break;
                case JpegXlColorEncoding.TransferLinear:
                    (curve, transfer) = (BuiltInIccCurve.Linear, "linear");
                    break;
                case JpegXlColorEncoding.TransferBt709:
                    (curve, transfer) = (BuiltInIccCurve.Bt709, "BT.709");
                    break;
                case JpegXlColorEncoding.TransferDci:
                    (curve, exponent, transfer) = (BuiltInIccCurve.Power, 2.6, "DCI");
                    break;
                default:
                    throw JpegXlFormat.Unsupported("The JPEG XL image combines the PQ or HLG transfer function with custom primaries or a custom white point.", "PQ or HLG with a custom color space");
            }
        }

        var white = encoding.WhitePoint switch
        {
            JpegXlColorEncoding.WhitePointD65 => "D65",
            JpegXlColorEncoding.WhitePointE => "E",
            JpegXlColorEncoding.WhitePointDci => "DCI",
            _ => "custom white point",
        };
        var description = white + ", " + transfer;
        if (!encoding.IsGray)
        {
            description = encoding.Primaries switch
            {
                JpegXlColorEncoding.PrimariesSrgb => "sRGB primaries, ",
                JpegXlColorEncoding.PrimariesRec2100 => "Rec. 2100 primaries, ",
                JpegXlColorEncoding.PrimariesP3 => "P3 primaries, ",
                _ => "Custom primaries, ",
            } + description;
        }

        Span<double> primaries = stackalloc double[6];
        encoding.GetPrimariesChromaticities(primaries);
        var (whiteX, whiteY) = encoding.GetWhitePointChromaticity();
        return BuiltInIccProfiles.TryCreate(encoding.IsGray, primaries, whiteX, whiteY, curve, exponent, encoding.RenderingIntent, description)
            ?? throw JpegXlFormat.Invalid("the primaries or the white point of the color encoding are not valid.");
    }

    private static void EnsureSupportedDepth(JpegXlBitDepth depth, string channel)
    {
        if (depth.IsFloat)
            throw JpegXlFormat.Unsupported($"The JPEG XL image has floating-point {channel} samples.", "floating-point samples");

        if (depth.BitsPerSample > 16)
            throw JpegXlFormat.Unsupported($"The JPEG XL image has {depth.BitsPerSample.ToString(CultureInfo.InvariantCulture)}-bit {channel} samples.", "samples of more than 16 bits");
    }
}
