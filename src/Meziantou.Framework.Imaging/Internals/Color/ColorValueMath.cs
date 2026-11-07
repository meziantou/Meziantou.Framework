namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants and the equations of the color value types (<see cref="CieXyz"/>, <see cref="CieLab"/>,
/// <see cref="Oklab"/>, ...), written from CSS Color Module Level 4 (section 10 "Predefined color spaces", section 18
/// "Sample code for color conversions") and from CIE 15:2004 (CIELAB, CIELUV).
/// </summary>
/// <remarks>
/// The matrices are the ones CSS Color 4 publishes: the rational sRGB matrices, the Bradford adaptation between D65 and
/// D50 computed with 64-bit precision, and the Oklab matrices recomputed for the D65 white of CSS. Each product of a matrix
/// and a vector adds its three terms from left to right, as the sample code does, so that the results are those of the
/// sample code to the last bits whenever the functions of the platform (<see cref="Math.Pow(double, double)"/>,
/// <see cref="Math.Cbrt(double)"/>, <see cref="Math.Atan2(double, double)"/>) give the same results.
/// </remarks>
internal static class ColorValueMath
{
    // CSS Color 4 section 10.1: the white points from their four-figure CIE chromaticities, Y = 1
    public const double D50X = 0.3457 / 0.3585;
    public const double D50Z = (1.0 - 0.3457 - 0.3585) / 0.3585;
    public const double D65X = 0.3127 / 0.3290;
    public const double D65Z = (1.0 - 0.3127 - 0.3290) / 0.3290;

    // CIE 15:2004 section 8.2.1, as exact fractions: (6/29)^3 and (29/3)^3
    public const double Epsilon = 216.0 / 24389;
    public const double Kappa = 24389.0 / 27;

    /// <summary>Linear sRGB to CIEXYZ relative to D65 (CSS Color 4, <c>lin_sRGB_to_XYZ</c>).</summary>
    public static readonly ColorValueMatrix LinearSrgbToXyzD65 = new(
        506752.0 / 1228815, 87881.0 / 245763, 12673.0 / 70218,
        87098.0 / 409605, 175762.0 / 245763, 12673.0 / 175545,
        7918.0 / 409605, 87881.0 / 737289, 1001167.0 / 1053270);

    /// <summary>CIEXYZ relative to D65 to linear sRGB (CSS Color 4, <c>XYZ_to_lin_sRGB</c>).</summary>
    public static readonly ColorValueMatrix XyzD65ToLinearSrgb = new(
        12831.0 / 3959, -329.0 / 214, -1974.0 / 3959,
        -851781.0 / 878810, 1648619.0 / 878810, 36519.0 / 878810,
        705.0 / 12673, -2585.0 / 12673, 705.0 / 667);

    /// <summary>The linear Bradford adaptation from D65 to D50 (CSS Color 4, <c>D65_to_D50</c>).</summary>
    public static readonly ColorValueMatrix D65ToD50 = new(
        1.0479297925449969, 0.022946870601609652, -0.05019226628920524,
        0.02962780877005599, 0.9904344267538799, -0.017073799063418826,
        -0.009243040646204504, 0.015055191490298152, 0.7518742814281371);

    /// <summary>The linear Bradford adaptation from D50 to D65 (CSS Color 4, <c>D50_to_D65</c>).</summary>
    public static readonly ColorValueMatrix D50ToD65 = new(
        0.955473421488075, -0.02309845494876471, 0.06325924320057072,
        -0.0283697093338637, 1.0099953980813041, 0.021041441191917323,
        0.012314014864481998, -0.020507649298898964, 1.330365926242124);

    /// <summary>CIEXYZ relative to D65 to the cone responses of Oklab (CSS Color 4, <c>XYZ_to_OKLab</c>).</summary>
    public static readonly ColorValueMatrix XyzD65ToLms = new(
        0.8190224379967030, 0.3619062600528904, -0.1288737815209879,
        0.0329836539323885, 0.9292868615863434, 0.0361446663506424,
        0.0481771893596242, 0.2642395317527308, 0.6335478284694309);

    /// <summary>The cube roots of the cone responses to Oklab (CSS Color 4, <c>XYZ_to_OKLab</c>).</summary>
    public static readonly ColorValueMatrix LmsToOklab = new(
        0.2104542683093140, 0.7936177747023054, -0.0040720430116193,
        1.9779985324311684, -2.4285922420485799, 0.4505937096174110,
        0.0259040424655478, 0.7827717124575296, -0.8086757549230774);

    /// <summary>Oklab to the cube roots of the cone responses (CSS Color 4, <c>OKLab_to_XYZ</c>).</summary>
    public static readonly ColorValueMatrix OklabToLms = new(
        1.0, 0.3963377773761749, 0.2158037573099136,
        1.0, -0.1055613458156586, -0.0638541728258133,
        1.0, -0.0894841775298119, -1.2914855480194092);

    /// <summary>The cone responses of Oklab to CIEXYZ relative to D65 (CSS Color 4, <c>OKLab_to_XYZ</c>).</summary>
    public static readonly ColorValueMatrix LmsToXyzD65 = new(
        1.2268798758459243, -0.5578149944602171, 0.2813910456659647,
        -0.0405757452148008, 1.1122868032803170, -0.0717110580655164,
        -0.0763729366746601, -0.4214933324022432, 1.5869240198367816);

    // The cone response matrix of the Bradford transform (the coefficients ChromaticityMath uses) and its inverse,
    // computed rather than taken from its published seven digits
    private static readonly ColorValueMatrix Bradford = new(0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296);
    private static readonly ColorValueMatrix BradfordInverse = Bradford.Invert();

    /// <summary>
    /// Computes the linear Bradford adaptation from a white to another: the cone responses are scaled by the ratio of
    /// the responses to the two whites.
    /// </summary>
    public static ColorValueMatrix GetBradfordAdaptation(double sourceX, double sourceY, double sourceZ, double destinationX, double destinationY, double destinationZ)
    {
        var (sourceRho, sourceGamma, sourceBeta) = Bradford.Transform(sourceX, sourceY, sourceZ);
        var (destinationRho, destinationGamma, destinationBeta) = Bradford.Transform(destinationX, destinationY, destinationZ);
        var scale = new ColorValueMatrix(destinationRho / sourceRho, 0, 0, 0, destinationGamma / sourceGamma, 0, 0, 0, destinationBeta / sourceBeta);
        return BradfordInverse.Multiply(scale.Multiply(Bradford));
    }

    /// <summary>Converts CIEXYZ to CIELAB relative to a white (CSS Color 4, <c>XYZ_to_Lab</c>; CIE 15:2004 equation 8.2).</summary>
    public static (double L, double A, double B) XyzToLab(double x, double y, double z, double whiteX, double whiteY, double whiteZ)
    {
        var fx = LabFunction(x / whiteX);
        var fy = LabFunction(y / whiteY);
        var fz = LabFunction(z / whiteZ);
        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));

        static double LabFunction(double value) => value > Epsilon ? Math.Cbrt(value) : ((Kappa * value) + 16) / 116;
    }

    /// <summary>Converts CIELAB relative to a white to CIEXYZ (CSS Color 4, <c>Lab_to_XYZ</c>).</summary>
    public static (double X, double Y, double Z) LabToXyz(double lightness, double a, double b, double whiteX, double whiteY, double whiteZ)
    {
        // L is kept out of L + 16 for the precision near black
        var lightness116 = lightness / 116;
        var a500 = a / 500;
        var b200 = b / 200;
        var fy = lightness116 + (16.0 / 116);
        var fx = fy + a500;
        var fz = fy - b200;
        var fx3 = fx * fx * fx;
        var fz3 = fz * fz * fz;
        var x = fx3 > Epsilon ? fx3 : (lightness116 + a500) * (116 / Kappa);
        var y = lightness > Kappa * Epsilon ? Cube((lightness + 16) / 116) : lightness / Kappa;
        var z = fz3 > Epsilon ? fz3 : (lightness116 - b200) * (116 / Kappa);
        return (x * whiteX, y * whiteY, z * whiteZ);

        static double Cube(double value) => value * value * value;
    }

    /// <summary>Converts CIEXYZ relative to D65 to Oklab (CSS Color 4, <c>XYZ_to_OKLab</c>).</summary>
    public static (double L, double A, double B) XyzD65ToOklab(double x, double y, double z)
    {
        var (l, m, s) = XyzD65ToLms.Transform(x, y, z);
        return LmsToOklab.Transform(Math.Cbrt(l), Math.Cbrt(m), Math.Cbrt(s));
    }

    /// <summary>Converts Oklab to CIEXYZ relative to D65 (CSS Color 4, <c>OKLab_to_XYZ</c>).</summary>
    public static (double X, double Y, double Z) OklabToXyzD65(double lightness, double a, double b)
    {
        var (l, m, s) = OklabToLms.Transform(lightness, a, b);
        return LmsToXyzD65.Transform(l * l * l, m * m * m, s * s * s);
    }

    /// <summary>Converts rectangular coordinates to a chroma and a hue in degrees, in [0, 360) (CSS Color 4, <c>Lab_to_LCH</c>).</summary>
    public static (double Chroma, double Hue) ToPolar(double a, double b)
    {
        var chroma = Math.Sqrt((a * a) + (b * b));
        var hue = Math.Atan2(b, a) * 180 / Math.PI;
        if (hue < 0)
        {
            hue += 360;
        }

        // A tiny negative angle becomes 360 once added: the range is half-open
        return (chroma, hue >= 360 ? hue - 360 : hue);
    }

    /// <summary>Converts a chroma and a hue in degrees to rectangular coordinates (CSS Color 4, <c>LCH_to_Lab</c>).</summary>
    public static (double A, double B) FromPolar(double chroma, double hue)
    {
        var radians = hue * Math.PI / 180;
        return (chroma * Math.Cos(radians), chroma * Math.Sin(radians));
    }

    /// <summary>Brings an angle in degrees to [0, 360).</summary>
    public static double NormalizeHue(double hue)
    {
        hue %= 360;
        if (hue < 0)
        {
            hue += 360;
        }

        return hue >= 360 ? hue - 360 : hue + 0.0;
    }

    /// <summary>
    /// Computes the hue of a color given by its red, green and blue values, in degrees in [0, 360), shared by HSL, HSV
    /// and HWB (CSS Color 4, <c>rgbToHsl</c> and <c>rgbToHue</c>); 0 for a gray, whose hue is powerless.
    /// </summary>
    public static double GetHue(double red, double green, double blue, double max, double min)
    {
        var delta = max - min;
        if (delta == 0)
            return 0;

        double hue;
        if (max == red)
        {
            hue = ((green - blue) / delta) + (green < blue ? 6 : 0);
        }
        else if (max == green)
        {
            hue = ((blue - red) / delta) + 2;
        }
        else
        {
            hue = ((red - green) / delta) + 4;
        }

        hue *= 60;
        return hue >= 360 ? hue - 360 : hue;
    }

    /// <summary>Computes the red, green and blue values of an HSL color, the hue in degrees (CSS Color 4, <c>hslToRgb</c>).</summary>
    public static (double Red, double Green, double Blue) HslToRgb(double hue, double saturation, double lightness)
    {
        // The equations need a hue in [0, 360)
        hue = NormalizeHue(hue);
        var a = saturation * Math.Min(lightness, 1 - lightness);
        return (Channel(0), Channel(8), Channel(4));

        double Channel(double n)
        {
            var k = (n + (hue / 30)) % 12;
            return lightness - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }
    }

    /// <summary>
    /// Interpolates between two hues in degrees along the arc a method selects (CSS Color 4 section 12.4, "Hue
    /// interpolation"): the hues are brought to [0, 360), one of them is moved by a turn when the arc needs it, and the
    /// result is brought back to [0, 360).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="method"/> is not a defined value.</exception>
    public static double InterpolateHue(double from, double to, double amount, HueInterpolation method)
    {
        from = NormalizeHue(from);
        to = NormalizeHue(to);
        var difference = to - from;
        switch (method)
        {
            case HueInterpolation.Shorter:
                if (difference > 180)
                {
                    from += 360;
                }
                else if (difference < -180)
                {
                    to += 360;
                }

                break;

            case HueInterpolation.Longer:
                if (difference > 0 && difference < 180)
                {
                    from += 360;
                }
                else if (difference > -180 && difference <= 0)
                {
                    to += 360;
                }

                break;

            case HueInterpolation.Increasing:
                if (difference < 0)
                {
                    to += 360;
                }

                break;

            case HueInterpolation.Decreasing:
                if (difference > 0)
                {
                    from += 360;
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "The hue interpolation method is not valid.");
        }

        return NormalizeHue(from + (amount * (to - from)));
    }

    /// <summary>Converts an encoded sRGB value to linear light, extended to negative values by symmetry (CSS Color 4, <c>lin_sRGB</c>).</summary>
    public static double DecodeSrgb(double value)
    {
        var abs = Math.Abs(value);
        if (abs <= 0.04045)
            return value / 12.92;

        var linear = Math.Pow((abs + 0.055) / 1.055, 2.4);
        return value < 0 ? -linear : linear;
    }

    /// <summary>Converts linear light to an encoded sRGB value, extended to negative values by symmetry (CSS Color 4, <c>gam_sRGB</c>).</summary>
    public static double EncodeSrgb(double value)
    {
        var abs = Math.Abs(value);
        if (abs > 0.0031308)
        {
            var encoded = (1.055 * Math.Pow(abs, 1 / 2.4)) - 0.055;
            return value < 0 ? -encoded : encoded;
        }

        return 12.92 * value;
    }

    /// <summary>Rounds a fraction of one to an 8-bit sample, clipped to [0, 255]; a value that is not a number is 0.</summary>
    public static byte ToByte(double value)
    {
        var scaled = Math.Round(value * byte.MaxValue, MidpointRounding.AwayFromZero);
        return scaled > 0 ? scaled < byte.MaxValue ? (byte)scaled : byte.MaxValue : (byte)0;
    }

    /// <summary>Rounds a fraction of one to a 16-bit sample, clipped to [0, 65535]; a value that is not a number is 0.</summary>
    public static ushort ToUInt16(double value)
    {
        var scaled = Math.Round(value * ushort.MaxValue, MidpointRounding.AwayFromZero);
        return scaled > 0 ? scaled < ushort.MaxValue ? (ushort)scaled : ushort.MaxValue : (ushort)0;
    }

    /// <summary>Formats a number for a hash code or a text: negative zero is zero.</summary>
    public static double Canonical(double value) => value == 0 ? 0 : value;
}
