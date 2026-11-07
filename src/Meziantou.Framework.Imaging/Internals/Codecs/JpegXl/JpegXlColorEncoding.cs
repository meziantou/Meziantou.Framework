namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <c>ColourEncoding</c> bundle of the image header (ISO/IEC 18181-1, section A.4): the color space of the image, as
/// enumerated values or as the announcement of an ICC profile that follows the header.
/// </summary>
internal sealed class JpegXlColorEncoding
{
    /// <summary>The scale of the custom chromaticity coordinates.</summary>
    public const double CustomXyScale = 1e-6;

    /// <summary>The scale of <see cref="Gamma"/>.</summary>
    public const double GammaScale = 1e-7;

    // Values of the color_space field
    public const int ColorSpaceRgb = 0;
    public const int ColorSpaceGray = 1;
    public const int ColorSpaceXyb = 2;
    public const int ColorSpaceUnknown = 3;

    // Values of the white_point field
    public const int WhitePointD65 = 1;
    public const int WhitePointCustom = 2;
    public const int WhitePointE = 10;
    public const int WhitePointDci = 11;

    // Values of the primaries field
    public const int PrimariesSrgb = 1;
    public const int PrimariesCustom = 2;
    public const int PrimariesRec2100 = 9;
    public const int PrimariesP3 = 11;

    // Values of the transfer_function field: they are the code points of ITU-T H.273
    public const int TransferBt709 = 1;
    public const int TransferUnknown = 2;
    public const int TransferLinear = 8;
    public const int TransferSrgb = 13;
    public const int TransferPq = 16;
    public const int TransferDci = 17;
    public const int TransferHlg = 18;

    private JpegXlColorEncoding()
    {
    }

    /// <summary>Gets the encoding of a header that only sets <c>all_default</c>: sRGB.</summary>
    public static JpegXlColorEncoding Default { get; } = new();

    /// <summary>Gets a value indicating whether an ICC profile follows the image header and describes the color space.</summary>
    public bool WantIcc { get; private init; }

    /// <summary>Gets the color space: RGB, grayscale, XYB or unknown.</summary>
    public int ColorSpace { get; private init; } = ColorSpaceRgb;

    /// <summary>Gets the white point.</summary>
    public int WhitePoint { get; private init; } = WhitePointD65;

    /// <summary>Gets the custom white point, in units of <see cref="CustomXyScale"/>.</summary>
    public (int X, int Y) CustomWhitePoint { get; private init; }

    /// <summary>Gets the primaries.</summary>
    public int Primaries { get; private init; } = PrimariesSrgb;

    /// <summary>Gets the custom red primary, in units of <see cref="CustomXyScale"/>.</summary>
    public (int X, int Y) CustomRed { get; private init; }

    /// <summary>Gets the custom green primary, in units of <see cref="CustomXyScale"/>.</summary>
    public (int X, int Y) CustomGreen { get; private init; }

    /// <summary>Gets the custom blue primary, in units of <see cref="CustomXyScale"/>.</summary>
    public (int X, int Y) CustomBlue { get; private init; }

    /// <summary>Gets a value indicating whether the transfer function is a power function of exponent <see cref="Gamma"/>.</summary>
    public bool HasGamma { get; private init; }

    /// <summary>Gets the exponent of the power function that encodes the samples, in units of <see cref="GammaScale"/>.</summary>
    public uint Gamma { get; private init; }

    /// <summary>Gets the enumerated transfer function, when <see cref="HasGamma"/> is <see langword="false"/>.</summary>
    public int TransferFunction { get; private init; } = TransferSrgb;

    /// <summary>Gets the rendering intent (0: perceptual, 1: relative, 2: saturation, 3: absolute).</summary>
    public int RenderingIntent { get; private init; } = 1;

    /// <summary>Gets a value indicating whether the color channels are a single gray channel.</summary>
    public bool IsGray => ColorSpace == ColorSpaceGray;

    /// <summary>Gets a value indicating whether the encoding is the sRGB color space (or its grayscale counterpart), white point and transfer function included.</summary>
    public bool IsSrgb => !WantIcc && ColorSpace is ColorSpaceRgb or ColorSpaceGray && WhitePoint == WhitePointD65 && (IsGray || Primaries == PrimariesSrgb) && !HasGamma && TransferFunction == TransferSrgb;

    /// <summary>Gets a value indicating whether the encoding is the sRGB primaries and white point with a linear transfer function.</summary>
    public bool IsLinearSrgb => !WantIcc && ColorSpace is ColorSpaceRgb or ColorSpaceGray && WhitePoint == WhitePointD65 && (IsGray || Primaries == PrimariesSrgb) && !HasGamma && TransferFunction == TransferLinear;

    /// <summary>Gets the chromaticity of the white point.</summary>
    public (double X, double Y) GetWhitePointChromaticity() => WhitePoint switch
    {
        WhitePointE => (1.0 / 3, 1.0 / 3),
        WhitePointDci => (0.314, 0.351),
        WhitePointCustom => (CustomWhitePoint.X * CustomXyScale, CustomWhitePoint.Y * CustomXyScale),
        _ => (0.3127, 0.3290),
    };

    /// <summary>Gets the chromaticities of the primaries: the x and y of red, green and blue.</summary>
    public void GetPrimariesChromaticities(Span<double> primaries)
    {
        ReadOnlySpan<double> values = Primaries switch
        {
            PrimariesRec2100 => [0.708, 0.292, 0.170, 0.797, 0.131, 0.046],
            PrimariesP3 => [0.680, 0.320, 0.265, 0.690, 0.150, 0.060],
            PrimariesCustom =>
            [
                CustomRed.X * CustomXyScale, CustomRed.Y * CustomXyScale,
                CustomGreen.X * CustomXyScale, CustomGreen.Y * CustomXyScale,
                CustomBlue.X * CustomXyScale, CustomBlue.Y * CustomXyScale,
            ],
            _ => [0.639998686, 0.330010138, 0.300003784, 0.600003357, 0.150002046, 0.059997204],
        };

        values.CopyTo(primaries);
    }

    public static JpegXlColorEncoding Read(JpegXlBitReader reader)
    {
        if (reader.ReadBool())
            return Default;

        var wantIcc = reader.ReadBool();
        var colorSpace = reader.ReadEnum();
        if (colorSpace > ColorSpaceUnknown)
            throw JpegXlFormat.Invalid("the color space is not a defined value.");

        if (wantIcc)
            return new JpegXlColorEncoding { WantIcc = true, ColorSpace = colorSpace };

        var whitePoint = WhitePointD65;
        (int X, int Y) customWhitePoint = default;
        if (colorSpace != ColorSpaceXyb)
        {
            whitePoint = reader.ReadEnum();
            if (whitePoint is not (WhitePointD65 or WhitePointCustom or WhitePointE or WhitePointDci))
                throw JpegXlFormat.Invalid("the white point is not a defined value.");

            if (whitePoint == WhitePointCustom)
            {
                customWhitePoint = ReadCustomXy(reader);
            }
        }

        var primaries = PrimariesSrgb;
        (int X, int Y) red = default;
        (int X, int Y) green = default;
        (int X, int Y) blue = default;
        if (colorSpace is not (ColorSpaceGray or ColorSpaceXyb))
        {
            primaries = reader.ReadEnum();
            if (primaries is not (PrimariesSrgb or PrimariesCustom or PrimariesRec2100 or PrimariesP3))
                throw JpegXlFormat.Invalid("the primaries are not a defined value.");

            if (primaries == PrimariesCustom)
            {
                red = ReadCustomXy(reader);
                green = ReadCustomXy(reader);
                blue = ReadCustomXy(reader);
            }
        }

        bool hasGamma;
        uint gamma = 0;
        var transferFunction = TransferSrgb;
        if (colorSpace == ColorSpaceXyb)
        {
            // The transfer function of XYB is implicit: a cube root
            hasGamma = true;
            gamma = 3333333;
        }
        else
        {
            hasGamma = reader.ReadBool();
            if (hasGamma)
            {
                gamma = reader.ReadBits(24);

                // The exponent is at most 1 and at least 1/8192
                if (gamma > 10_000_000 || (ulong)gamma * 8192 < 10_000_000)
                    throw JpegXlFormat.Invalid("the gamma of the transfer function is out of range.");
            }
            else
            {
                transferFunction = reader.ReadEnum();
                if (transferFunction is not (TransferBt709 or TransferUnknown or TransferLinear or TransferSrgb or TransferPq or TransferDci or TransferHlg))
                    throw JpegXlFormat.Invalid("the transfer function is not a defined value.");
            }
        }

        var renderingIntent = reader.ReadEnum();
        if (renderingIntent > 3)
            throw JpegXlFormat.Invalid("the rendering intent is not a defined value.");

        if (colorSpace == ColorSpaceUnknown || (!hasGamma && transferFunction == TransferUnknown))
            throw JpegXlFormat.Invalid("the color space or the transfer function is unknown and no ICC profile describes it.");

        return new JpegXlColorEncoding
        {
            ColorSpace = colorSpace,
            WhitePoint = whitePoint,
            CustomWhitePoint = customWhitePoint,
            Primaries = primaries,
            CustomRed = red,
            CustomGreen = green,
            CustomBlue = blue,
            HasGamma = hasGamma,
            Gamma = gamma,
            TransferFunction = transferFunction,
            RenderingIntent = renderingIntent,
        };
    }

    private static (int X, int Y) ReadCustomXy(JpegXlBitReader reader)
    {
        var x = JpegXlBitReader.UnpackSigned(reader.ReadU32(0, 19, 524288, 19, 1048576, 20, 2097152, 21));
        var y = JpegXlBitReader.UnpackSigned(reader.ReadU32(0, 19, 524288, 19, 1048576, 20, 2097152, 21));
        return (x, y);
    }
}
