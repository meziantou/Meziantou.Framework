using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>The components of a color in a color space: the channels and the alpha are null when they are missing (none).</summary>
/// <param name="Space">
/// The color space: srgb, srgb-linear, display-p3, display-p3-linear, a98-rgb, prophoto-rgb, rec2020, rec2100-pq, rec2100-hlg,
/// rec2100-linear, xyz-d65, xyz-d50, hsl, hwb, lab, lch, oklab, oklch, ictcp, jzazbz or jzczhz. The channels are in the
/// reference ranges of the color functions (e.g. 0 to 1 for srgb, degrees and percentages for hsl).
/// </param>
internal readonly record struct CssColorComponents(string Space, double?[] Channels, double? Alpha);

/// <summary>
/// Converts colors between the color spaces (https://drafts.csswg.org/css-color-4/#color-conversion) with the sample code of
/// https://drafts.csswg.org/css-color-4/#color-conversion-code, including the handling of the missing components that are
/// carried forward (https://drafts.csswg.org/css-color-4/#interpolation-missing) and of the powerless components
/// (https://drafts.csswg.org/css-color-4/#powerless).
/// </summary>
internal static partial class CssColorConversion
{
    // The analogous components (https://drafts.csswg.org/css-color-4/#analogous-components); 0 is a component without analog
    private const byte None = 0;
    private const byte Red = 1;
    private const byte Green = 2;
    private const byte Blue = 3;
    private const byte Lightness = 4;
    private const byte Colorfulness = 5;
    private const byte Hue = 6;
    private const byte OpponentA = 7;
    private const byte OpponentB = 8;

    private static readonly byte[] RgbCategories = [Red, Green, Blue];
    private static readonly byte[] HslCategories = [Hue, Colorfulness, Lightness];
    private static readonly byte[] HwbCategories = [Hue, None, None];
    private static readonly byte[] LabCategories = [Lightness, OpponentA, OpponentB];
    private static readonly byte[] LchCategories = [Lightness, Colorfulness, Hue];

    // https://drafts.csswg.org/css-color-hdr-1/#ICtCp: Ct is the yellowness-blueness axis (similar to b) and Cp the
    // redness-greenness axis (similar to a)
    private static readonly byte[] IctcpCategories = [Lightness, OpponentB, OpponentA];

    private static readonly double[][] LinearSrgbToXyz =
    [
        [506752.0 / 1228815, 87881.0 / 245763, 12673.0 / 70218],
        [87098.0 / 409605, 175762.0 / 245763, 12673.0 / 175545],
        [7918.0 / 409605, 87881.0 / 737289, 1001167.0 / 1053270],
    ];

    private static readonly double[][] XyzToLinearSrgb =
    [
        [12831.0 / 3959, -329.0 / 214, -1974.0 / 3959],
        [-851781.0 / 878810, 1648619.0 / 878810, 36519.0 / 878810],
        [705.0 / 12673, -2585.0 / 12673, 705.0 / 667],
    ];

    private static readonly double[][] LinearP3ToXyz =
    [
        [608311.0 / 1250200, 189793.0 / 714400, 198249.0 / 1000160],
        [35783.0 / 156275, 247089.0 / 357200, 198249.0 / 2500400],
        [0, 32229.0 / 714400, 5220557.0 / 5000800],
    ];

    private static readonly double[][] XyzToLinearP3 =
    [
        [446124.0 / 178915, -333277.0 / 357830, -72051.0 / 178915],
        [-14852.0 / 17905, 63121.0 / 35810, 423.0 / 17905],
        [11844.0 / 330415, -50337.0 / 660830, 316169.0 / 330415],
    ];

    // ProPhoto RGB is relative to D50
    private static readonly double[][] LinearProPhotoToXyzD50 =
    [
        [0.79776664490064230, 0.13518129740053308, 0.03134773412839220],
        [0.28807482881940130, 0.71183523424187300, 0.00008993693872564],
        [0, 0, 0.82510460251046020],
    ];

    private static readonly double[][] XyzD50ToLinearProPhoto =
    [
        [1.34578688164715830, -0.25557208737979464, -0.05110186497554526],
        [-0.54463070512490190, 1.50824774284514680, 0.02052744743642139],
        [0, 0, 1.21196754563894520],
    ];

    private static readonly double[][] LinearA98RgbToXyz =
    [
        [573536.0 / 994567, 263643.0 / 1420810, 187206.0 / 994567],
        [591459.0 / 1989134, 6239551.0 / 9945670, 374412.0 / 4972835],
        [53769.0 / 1989134, 351524.0 / 4972835, 4929758.0 / 4972835],
    ];

    private static readonly double[][] XyzToLinearA98Rgb =
    [
        [1829569.0 / 896150, -506331.0 / 896150, -308931.0 / 896150],
        [-851781.0 / 878810, 1648619.0 / 878810, 36519.0 / 878810],
        [16779.0 / 1248040, -147721.0 / 1248040, 1266979.0 / 1248040],
    ];

    private static readonly double[][] LinearRec2020ToXyz =
    [
        [63426534.0 / 99577255, 20160776.0 / 139408157, 47086771.0 / 278816314],
        [26158966.0 / 99577255, 472592308.0 / 697040785, 8267143.0 / 139408157],
        [0, 19567812.0 / 697040785, 295819943.0 / 278816314],
    ];

    private static readonly double[][] XyzToLinearRec2020 =
    [
        [30757411.0 / 17917100, -6372589.0 / 17917100, -4539589.0 / 17917100],
        [-19765991.0 / 29648200, 47925759.0 / 29648200, 467509.0 / 29648200],
        [792561.0 / 44930125, -1921689.0 / 44930125, 42328811.0 / 44930125],
    ];

    private static readonly double[][] XyzToLms =
    [
        [0.8190224379967030, 0.3619062600528904, -0.1288737815209879],
        [0.0329836539323885, 0.9292868615863434, 0.0361446663506424],
        [0.0481771893596242, 0.2642395317527308, 0.6335478284694309],
    ];

    private static readonly double[][] LmsToOklab =
    [
        [0.2104542683093140, 0.7936177747023054, -0.0040720430116193],
        [1.9779985324311684, -2.4285922420485799, 0.4505937096174110],
        [0.0259040424655478, 0.7827717124575296, -0.8086757549230774],
    ];

    private static readonly double[][] OklabToLms =
    [
        [1.0, 0.3963377773761749, 0.2158037573099136],
        [1.0, -0.1055613458156586, -0.0638541728258133],
        [1.0, -0.0894841775298119, -1.2914855480194092],
    ];

    private static readonly double[][] LmsToXyz =
    [
        [1.2268798758459243, -0.5578149944602171, 0.2813910456659647],
        [-0.0405757452148008, 1.1122868032803170, -0.0717110580655164],
        [-0.0763729366746601, -0.4214933324022432, 1.5869240198367816],
    ];

    private static readonly double[][] D65ToD50 =
    [
        [1.0479297925449969, 0.022946870601609652, -0.05019226628920524],
        [0.02962780877005599, 0.9904344267538799, -0.017073799063418826],
        [-0.009243040646204504, 0.015055191490298152, 0.7518742814281371],
    ];

    private static readonly double[][] D50ToD65 =
    [
        [0.955473421488075, -0.02309845494876471, 0.06325924320057072],
        [-0.0283697093338637, 1.0099953980813041, 0.021041441191917323],
        [0.012314014864481998, -0.020507649298898964, 1.330365926242124],
    ];

    private static readonly double[] D50White = [0.3457 / 0.3585, 1.0, (1.0 - 0.3457 - 0.3585) / 0.3585];

    /// <summary>Returns whether the color space is supported (the color spaces of color() and of the color functions).</summary>
    public static bool IsSupportedSpace(string space) => GetCategories(space) is not null;

    /// <summary>Returns whether the color space is a cylindrical polar color space (https://drafts.csswg.org/css-color-4/#cylindrical-polar-color).</summary>
    public static bool IsPolar(string space) => space is "hsl" or "hwb" or "lch" or "oklch" or "jzczhz";

    /// <summary>Returns the index of the hue channel of a cylindrical polar color space, or -1.</summary>
    public static int GetHueIndex(string space) => space switch
    {
        "hsl" or "hwb" => 0,
        "lch" or "oklch" or "jzczhz" => 2,
        _ => -1,
    };

    /// <summary>Returns the components of a parsed color, or null when its color space is not supported (e.g. a custom color space).</summary>
    public static CssColorComponents? FromColor(CssColor color)
    {
        if (color.SourceSpace is { } sourceSpace && color.SourceChannels is { } sourceChannels)
            return new CssColorComponents(sourceSpace, [.. sourceChannels], color.Alpha);

        switch (color.Function)
        {
            case "rgb":
                return new CssColorComponents("srgb", [.. color.Channels.Select(c => c / 255)], color.Alpha);

            case "device-cmyk":
            {
                // https://drafts.csswg.org/css-color-5/#cmyk-rgb: the naive conversion
                var c = color.Channels.Select(v => v ?? 0).ToArray();
                return new CssColorComponents("srgb", [1 - Math.Min(1, (c[0] * (1 - c[3])) + c[3]), 1 - Math.Min(1, (c[1] * (1 - c[3])) + c[3]), 1 - Math.Min(1, (c[2] * (1 - c[3])) + c[3])], color.Alpha);
            }

            default:
                return IsSupportedSpace(color.Function) && color.Channels.Length == 3 ? new CssColorComponents(color.Function, [.. color.Channels], color.Alpha) : null;
        }
    }

    /// <summary>
    /// Returns the color of the components: the colors in hsl and hwb are sRGB colors (serialized as color(srgb)), unless
    /// they have missing components (https://drafts.csswg.org/css-color-4/#serializing-sRGB-values).
    /// </summary>
    public static CssColor ToColor(CssColorComponents color)
    {
        if (color.Space is "hsl" or "hwb" && Array.TrueForAll(color.Channels, c => c is not null) && color.Alpha is not null)
        {
            var rgb = ToSrgb(color.Space, [.. color.Channels.Select(c => c ?? 0)]);
            return CssColor.Create("srgb", [rgb[0], rgb[1], rgb[2]], color.Alpha, color.Space, color.Channels);
        }

        return CssColor.Create(color.Space, color.Channels, color.Alpha);
    }

    /// <summary>
    /// Converts a color to another color space (https://drafts.csswg.org/css-color-4/#color-conversion): the missing components
    /// that have an analog in the destination color space are carried forward, and the powerless components of the result are
    /// missing. The color is returned as is when it is already in the color space.
    /// </summary>
    public static CssColorComponents Convert(CssColorComponents color, string space)
    {
        if (color.Space == space)
            return color;

        var carried = GetCarriedForwardComponents(color, space);

        // https://drafts.csswg.org/css-color-4/#color-convert-algorithm: prepare the color for conversion (the powerless
        // components are missing, i.e. zero) and replace the missing components with zero
        var source = new double[3];
        for (var i = 0; i < 3; i++)
        {
            source[i] = color.Channels[i] ?? 0;
        }

        MakePowerlessComponentsMissing(color.Space, source, color.Channels);

        // The conversions between sRGB, hsl and hwb do not use XYZ (e.g. white is achromatic in hsl)
        var result = IsSrgbSpace(color.Space) && IsSrgbSpace(space) ? FromSrgb(space, ToSrgb(color.Space, source)) : FromXyz(space, ToXyz(color.Space, source));
        var channels = new double?[3];
        for (var i = 0; i < 3; i++)
        {
            channels[i] = carried[i] || double.IsNaN(result[i]) ? null : result[i];
        }

        return new CssColorComponents(space, channels, color.Alpha);
    }

    /// <summary>Returns the components of the destination color space that are missing because their analog is missing (https://drafts.csswg.org/css-color-4/#interpolation-missing).</summary>
    private static bool[] GetCarriedForwardComponents(CssColorComponents color, string space)
    {
        var sourceCategories = GetCategories(color.Space)!;
        var destinationCategories = GetCategories(space)!;
        var carried = new bool[3];
        var sourceRemaining = new List<int>(3);
        for (var i = 0; i < 3; i++)
        {
            var category = sourceCategories[i];
            var index = category == None ? -1 : Array.IndexOf(destinationCategories, category);
            if (index < 0)
            {
                sourceRemaining.Add(i);
            }
            else if (color.Channels[i] is null)
            {
                carried[index] = true;
            }
        }

        // The components that remain after removing the analogous components form an analogous set: they are carried forward
        // when they are all missing
        if (sourceRemaining.Count > 0 && sourceRemaining.TrueForAll(i => color.Channels[i] is null))
        {
            for (var i = 0; i < 3; i++)
            {
                var category = destinationCategories[i];
                if (category == None || Array.IndexOf(sourceCategories, category) < 0)
                {
                    carried[i] = true;
                }
            }
        }

        return carried;
    }

    /// <summary>
    /// The powerless components of a color that is converted are missing (https://drafts.csswg.org/css-color-4/#powerless):
    /// the colorfulness of a powerless hue is set to zero, and the achromatic hwb colors are normalized.
    /// </summary>
    private static void MakePowerlessComponentsMissing(string space, double[] channels, double?[] specified)
    {
        switch (space)
        {
            case "hsl":
                if (channels[1] > 0 && channels[1] <= 0.001)
                {
                    channels[1] = 0;
                }

                break;

            case "lch" or "oklch" or "jzczhz":
            {
                var epsilon = space switch
                {
                    "lch" => 0.0015,
                    "oklch" => 0.000004,
                    _ => JzczhzEpsilon,
                };
                if (channels[1] > 0 && channels[1] <= epsilon)
                {
                    channels[1] = 0;
                }

                break;
            }

            case "hwb":
            {
                var sum = channels[1] + channels[2];
                if (sum >= 99.999 && sum < 100)
                {
                    if (specified[1] is not null && specified[2] is not null)
                    {
                        channels[2] = 100 - channels[1];
                    }
                    else if (specified[1] is not null)
                    {
                        channels[1] = 100;
                    }
                    else if (specified[2] is not null)
                    {
                        channels[2] = 100;
                    }
                }

                break;
            }
        }
    }

    private static byte[]? GetCategories(string space) => space switch
    {
        "srgb" or "srgb-linear" or "display-p3" or "display-p3-linear" or "a98-rgb" or "prophoto-rgb" or "rec2020" or "rec2100-pq" or "rec2100-hlg" or "rec2100-linear" or "xyz-d65" or "xyz-d50" => RgbCategories,
        "hsl" => HslCategories,
        "hwb" => HwbCategories,
        "lab" or "oklab" or "jzazbz" => LabCategories,
        "lch" or "oklch" or "jzczhz" => LchCategories,
        "ictcp" => IctcpCategories,
        _ => null,
    };

    /// <summary>Converts a color to sRGB (gamma-encoded, 0 to 1).</summary>
    public static double[] ToSrgb(string space, double[] channels) => space switch
    {
        "srgb" => channels,
        "hsl" => HslToSrgb(channels),
        "hwb" => HwbToSrgb(channels),
        _ => FromXyz("srgb", ToXyz(space, channels)),
    };

    private static bool IsSrgbSpace(string space) => space is "srgb" or "hsl" or "hwb";

    private static double[] FromSrgb(string space, double[] rgb) => space switch
    {
        "hsl" => SrgbToHsl(rgb),
        "hwb" => SrgbToHwb(rgb),
        _ => rgb,
    };

    /// <summary>Converts a color to CIE XYZ (D65).</summary>
    public static double[] ToXyz(string space, double[] c) => space switch
    {
        "srgb" => Multiply(LinearSrgbToXyz, Map(c, SrgbToLinear)),
        "hsl" => Multiply(LinearSrgbToXyz, Map(HslToSrgb(c), SrgbToLinear)),
        "hwb" => Multiply(LinearSrgbToXyz, Map(HwbToSrgb(c), SrgbToLinear)),
        "srgb-linear" => Multiply(LinearSrgbToXyz, c),
        "display-p3" => Multiply(LinearP3ToXyz, Map(c, SrgbToLinear)),
        "display-p3-linear" => Multiply(LinearP3ToXyz, c),
        "a98-rgb" => Multiply(LinearA98RgbToXyz, Map(c, v => Math.CopySign(Math.Pow(Math.Abs(v), 563.0 / 256), v))),
        "prophoto-rgb" => Multiply(D50ToD65, Multiply(LinearProPhotoToXyzD50, Map(c, ProPhotoToLinear))),
        "rec2020" => Multiply(LinearRec2020ToXyz, Map(c, Rec2020ToLinear)),
        "xyz-d65" => c,
        "xyz-d50" => Multiply(D50ToD65, c),
        "lab" => Multiply(D50ToD65, LabToXyzD50(c)),
        "lch" => Multiply(D50ToD65, LabToXyzD50(LchToLab(c))),
        "oklab" => OklabToXyz(c),
        "oklch" => OklabToXyz(LchToLab(c)),
        _ => HdrToXyz(space, c) ?? throw new ArgumentOutOfRangeException(nameof(space), space, message: null),
    };

    /// <summary>Converts a color from CIE XYZ (D65); a powerless hue is NaN.</summary>
    public static double[] FromXyz(string space, double[] xyz) => space switch
    {
        "srgb" => Map(Multiply(XyzToLinearSrgb, xyz), LinearToSrgb),
        "hsl" => SrgbToHsl(Map(Multiply(XyzToLinearSrgb, xyz), LinearToSrgb)),
        "hwb" => SrgbToHwb(Map(Multiply(XyzToLinearSrgb, xyz), LinearToSrgb)),
        "srgb-linear" => Multiply(XyzToLinearSrgb, xyz),
        "display-p3" => Map(Multiply(XyzToLinearP3, xyz), LinearToSrgb),
        "display-p3-linear" => Multiply(XyzToLinearP3, xyz),
        "a98-rgb" => Map(Multiply(XyzToLinearA98Rgb, xyz), v => Math.CopySign(Math.Pow(Math.Abs(v), 256.0 / 563), v)),
        "prophoto-rgb" => Map(Multiply(XyzD50ToLinearProPhoto, Multiply(D65ToD50, xyz)), LinearToProPhoto),
        "rec2020" => Map(Multiply(XyzToLinearRec2020, xyz), LinearToRec2020),
        "xyz-d65" => xyz,
        "xyz-d50" => Multiply(D65ToD50, xyz),
        "lab" => XyzD50ToLab(Multiply(D65ToD50, xyz)),
        "lch" => LabToLch(XyzD50ToLab(Multiply(D65ToD50, xyz)), 0.0015),
        "oklab" => XyzToOklab(xyz),
        "oklch" => LabToLch(XyzToOklab(xyz), 0.000004),
        _ => XyzToHdr(space, xyz) ?? throw new ArgumentOutOfRangeException(nameof(space), space, message: null),
    };

    private static double[] Map(double[] values, Func<double, double> func) => [func(values[0]), func(values[1]), func(values[2])];

    private static double SrgbToLinear(double value)
    {
        var abs = Math.Abs(value);
        return abs <= 0.04045 ? value / 12.92 : Math.CopySign(Math.Pow((abs + 0.055) / 1.055, 2.4), value);
    }

    private static double LinearToSrgb(double value)
    {
        var abs = Math.Abs(value);
        return abs > 0.0031308 ? Math.CopySign((1.055 * Math.Pow(abs, 1 / 2.4)) - 0.055, value) : 12.92 * value;
    }

    // The rec2020 transfer function is the piecewise function of ITU-R BT.2020 (extended to the negative values), as in the
    // browsers and in the previous drafts: the current draft (https://drafts.csswg.org/css-color-4/#predefined-rec2020) uses
    // the pure 2.4 gamma of ITU-R BT.1886, which no browser implements
    private const double Rec2020Alpha = 1.09929682680944;
    private const double Rec2020Beta = 0.018053968510807;

    private static double Rec2020ToLinear(double value)
    {
        var abs = Math.Abs(value);
        return abs < Rec2020Beta * 4.5 ? value / 4.5 : Math.CopySign(Math.Pow((abs + Rec2020Alpha - 1) / Rec2020Alpha, 1 / 0.45), value);
    }

    private static double LinearToRec2020(double value)
    {
        var abs = Math.Abs(value);
        return abs >= Rec2020Beta ? Math.CopySign((Rec2020Alpha * Math.Pow(abs, 0.45)) - (Rec2020Alpha - 1), value) : 4.5 * value;
    }

    private static double ProPhotoToLinear(double value)
    {
        var abs = Math.Abs(value);
        return abs <= 16.0 / 512 ? value / 16 : Math.CopySign(Math.Pow(abs, 1.8), value);
    }

    private static double LinearToProPhoto(double value)
    {
        var abs = Math.Abs(value);
        return abs >= 1.0 / 512 ? Math.CopySign(Math.Pow(abs, 1 / 1.8), value) : 16 * value;
    }

    private static double[] HslToSrgb(double[] hsl)
    {
        var (r, g, b) = CssColor.HslToRgb(hsl[0], hsl[1] / 100, hsl[2] / 100);
        return [r, g, b];
    }

    private static double[] HwbToSrgb(double[] hwb)
    {
        var (r, g, b) = CssColor.HwbToRgb(hwb[0], hwb[1] / 100, hwb[2] / 100);
        return [r, g, b];
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#rgb-to-hsl (better-rgbToHsl.js)</summary>
    private static double[] SrgbToHsl(double[] rgb)
    {
        const double LightnessEpsilon = 1e-12;
        var (red, green, blue) = (rgb[0], rgb[1], rgb[2]);
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var hue = double.NaN;
        var saturation = 0.0;
        var lightness = (min + max) / 2;
        var d = max - min;
        if (d != 0)
        {
            // The saturation of a lightness of 0% or 100% is zero; a lightness that differs from it by a rounding error (e.g.
            // white converted from lch) would give a saturation that is a ratio of rounding errors
            saturation = Math.Abs(lightness) <= LightnessEpsilon || Math.Abs(1 - lightness) <= LightnessEpsilon ? 0 : (max - lightness) / Math.Min(lightness, 1 - lightness);
            if (max == red)
            {
                hue = ((green - blue) / d) + (green < blue ? 6 : 0);
            }
            else if (max == green)
            {
                hue = ((blue - red) / d) + 2;
            }
            else
            {
                hue = ((red - green) / d) + 4;
            }

            hue *= 60;
        }

        // Very out of gamut colors can produce negative saturation: the hue is rotated by 180 (https://github.com/w3c/csswg-drafts/issues/9222)
        if (saturation < 0)
        {
            hue += 180;
            saturation = Math.Abs(saturation);
        }

        if (hue >= 360)
        {
            hue -= 360;
        }

        if (saturation <= 1.0 / 100000)
        {
            hue = double.NaN;
            saturation = 0;
        }

        return [hue, saturation * 100, lightness * 100];
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#rgb-to-hwb</summary>
    private static double[] SrgbToHwb(double[] rgb)
    {
        var (red, green, blue) = (rgb[0], rgb[1], rgb[2]);
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var hue = double.NaN;
        var d = max - min;
        if (d != 0)
        {
            if (max == red)
            {
                hue = ((green - blue) / d) + (green < blue ? 6 : 0);
            }
            else if (max == green)
            {
                hue = ((blue - red) / d) + 2;
            }
            else
            {
                hue = ((red - green) / d) + 4;
            }

            hue *= 60;
            if (hue >= 360)
            {
                hue -= 360;
            }
        }

        var white = min;
        var black = 1 - max;
        // A powerless hue is missing and the color is normalized to an achromatic color (https://drafts.csswg.org/css-color-4/#powerless)
        if (white + black >= 1 - (1.0 / 100000))
        {
            hue = double.NaN;
            if (white + black < 1)
            {
                black = 1 - white;
            }
        }

        return [hue, white * 100, black * 100];
    }

    private static double[] XyzToOklab(double[] xyz)
    {
        var lms = Multiply(XyzToLms, xyz);
        return Multiply(LmsToOklab, [Math.Cbrt(lms[0]), Math.Cbrt(lms[1]), Math.Cbrt(lms[2])]);
    }

    private static double[] OklabToXyz(double[] lab)
    {
        var lms = Multiply(OklabToLms, lab);
        return Multiply(LmsToXyz, [lms[0] * lms[0] * lms[0], lms[1] * lms[1] * lms[1], lms[2] * lms[2] * lms[2]]);
    }

    private static double[] XyzD50ToLab(double[] xyz)
    {
        const double Epsilon = 216.0 / 24389;
        const double Kappa = 24389.0 / 27;
        var f = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var value = xyz[i] / D50White[i];
            f[i] = value > Epsilon ? Math.Cbrt(value) : ((Kappa * value) + 16) / 116;
        }

        return [(116 * f[1]) - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])];
    }

    private static double[] LabToXyzD50(double[] lab)
    {
        const double Epsilon = 216.0 / 24389;
        const double Kappa = 24389.0 / 27;
        var f1 = (lab[0] + 16) / 116;
        var f0 = (lab[1] / 500) + f1;
        var f2 = f1 - (lab[2] / 200);
        var x = Math.Pow(f0, 3) > Epsilon ? Math.Pow(f0, 3) : ((116 * f0) - 16) / Kappa;
        var y = lab[0] > Kappa * Epsilon ? Math.Pow((lab[0] + 16) / 116, 3) : lab[0] / Kappa;
        var z = Math.Pow(f2, 3) > Epsilon ? Math.Pow(f2, 3) : ((116 * f2) - 16) / Kappa;
        return [x * D50White[0], y * D50White[1], z * D50White[2]];
    }

    private static double[] LchToLab(double[] lch)
    {
        var hue = lch[2] * Math.PI / 180;
        return [lch[0], lch[1] * Math.Cos(hue), lch[1] * Math.Sin(hue)];
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#lab-to-lch: the hue is powerless (NaN) when the chroma is at most epsilon, and the chroma is then zero.</summary>
    private static double[] LabToLch(double[] lab, double epsilon)
    {
        var chroma = Math.Sqrt((lab[1] * lab[1]) + (lab[2] * lab[2]));
        if (chroma <= epsilon)
            return [lab[0], 0, double.NaN];

        var hue = Math.Atan2(lab[2], lab[1]) * 180 / Math.PI;
        return [lab[0], chroma, hue < 0 ? hue + 360 : hue];
    }

    private static double[] Multiply(double[][] matrix, double[] vector) =>
    [
        (matrix[0][0] * vector[0]) + (matrix[0][1] * vector[1]) + (matrix[0][2] * vector[2]),
        (matrix[1][0] * vector[0]) + (matrix[1][1] * vector[1]) + (matrix[1][2] * vector[2]),
        (matrix[2][0] * vector[0]) + (matrix[2][1] * vector[1]) + (matrix[2][2] * vector[2]),
    ];
}
