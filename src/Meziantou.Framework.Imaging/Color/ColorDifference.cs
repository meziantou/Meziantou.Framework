namespace Meziantou.Framework.Imaging;

/// <summary>
/// Color difference metrics (ΔE): how different two colors look, as a distance in CIELAB or Oklab. A difference of
/// about 1 in CIELAB (about 0.02 in Oklab) is just noticeable.
/// </summary>
/// <remarks>
/// The metrics take the colors as they are: give them relative to the same white (<see cref="CieLab.FromRgb(Rgba32, RgbColorSpace?)"/>
/// gives D50). <see cref="DeltaE94"/> is not symmetric: its weights come from the reference color.
/// </remarks>
public static class ColorDifference
{
    /// <summary>Computes the CIE 1976 color difference (ΔE*ab, ΔE76): the Euclidean distance in CIELAB.</summary>
    /// <param name="reference">The first color.</param>
    /// <param name="sample">The second color.</param>
    /// <returns>The difference.</returns>
    public static double DeltaE76(CieLab reference, CieLab sample)
    {
        var deltaL = reference.L - sample.L;
        var deltaA = reference.A - sample.A;
        var deltaB = reference.B - sample.B;
        return Math.Sqrt((deltaL * deltaL) + (deltaA * deltaA) + (deltaB * deltaB));
    }

    /// <summary>
    /// Computes the CIE 1994 color difference (ΔE*94, CIE 116-1995): the differences of lightness, chroma and hue,
    /// weighted by the chroma of the reference color.
    /// </summary>
    /// <param name="reference">The reference color, whose chroma weights the differences.</param>
    /// <param name="sample">The color compared with the reference.</param>
    /// <param name="application">The weights of the kind of samples compared.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="application"/> is not a defined value.</exception>
    public static double DeltaE94(CieLab reference, CieLab sample, Cie94Application application = Cie94Application.GraphicArts)
    {
        var (lightnessWeight, k1, k2) = application switch
        {
            Cie94Application.GraphicArts => (1.0, 0.045, 0.015),
            Cie94Application.Textiles => (2.0, 0.048, 0.014),
            _ => throw new ArgumentOutOfRangeException(nameof(application), application, "The application is not valid."),
        };

        var referenceChroma = Math.Sqrt((reference.A * reference.A) + (reference.B * reference.B));
        var sampleChroma = Math.Sqrt((sample.A * sample.A) + (sample.B * sample.B));
        var deltaL = reference.L - sample.L;
        var deltaA = reference.A - sample.A;
        var deltaB = reference.B - sample.B;
        var deltaC = referenceChroma - sampleChroma;

        // The square of the hue difference is what remains of the distance in the a-b plane once the chroma difference
        // is removed; rounding can make it slightly negative
        var deltaHSquared = Math.Max(0, (deltaA * deltaA) + (deltaB * deltaB) - (deltaC * deltaC));
        var chromaScale = 1 + (k1 * referenceChroma);
        var hueScale = 1 + (k2 * referenceChroma);
        var lightnessTerm = deltaL / lightnessWeight;
        var chromaTerm = deltaC / chromaScale;
        return Math.Sqrt((lightnessTerm * lightnessTerm) + (chromaTerm * chromaTerm) + (deltaHSquared / (hueScale * hueScale)));
    }

    /// <summary>
    /// Computes the CIEDE2000 color difference (ΔE00, CIE 142-2001), as Sharma, Wu and Dalal state its equations ("The
    /// CIEDE2000 color-difference formula: implementation notes, supplementary test data, and mathematical observations",
    /// 2005).
    /// </summary>
    /// <param name="reference">The first color.</param>
    /// <param name="sample">The second color.</param>
    /// <param name="lightnessWeight">The parametric factor kL of the viewing conditions. Defaults to 1.</param>
    /// <param name="chromaWeight">The parametric factor kC of the viewing conditions. Defaults to 1.</param>
    /// <param name="hueWeight">The parametric factor kH of the viewing conditions. Defaults to 1.</param>
    /// <returns>The difference. It is symmetric.</returns>
    public static double DeltaE2000(CieLab reference, CieLab sample, double lightnessWeight = 1, double chromaWeight = 1, double hueWeight = 1)
    {
        const double Pow25To7 = 6103515625; // 25^7
        const double DegreesToRadians = Math.PI / 180;

        // Equations 2 to 7: the a axis is stretched near the neutral axis, which gives new chromas and hues
        var chroma1 = Math.Sqrt((reference.A * reference.A) + (reference.B * reference.B));
        var chroma2 = Math.Sqrt((sample.A * sample.A) + (sample.B * sample.B));
        var meanChroma7 = Math.Pow((chroma1 + chroma2) / 2, 7);
        var g = 0.5 * (1 - Math.Sqrt(meanChroma7 / (meanChroma7 + Pow25To7)));
        var a1 = (1 + g) * reference.A;
        var a2 = (1 + g) * sample.A;
        var c1 = Math.Sqrt((a1 * a1) + (reference.B * reference.B));
        var c2 = Math.Sqrt((a2 * a2) + (sample.B * sample.B));
        var h1 = Hue(reference.B, a1);
        var h2 = Hue(sample.B, a2);

        // Equations 8 to 11: the differences of lightness, chroma and hue
        var deltaL = sample.L - reference.L;
        var deltaC = c2 - c1;
        var chromaProduct = c1 * c2;
        double deltaHueAngle;
        if (chromaProduct == 0)
        {
            deltaHueAngle = 0;
        }
        else
        {
            deltaHueAngle = h2 - h1;
            if (deltaHueAngle > 180)
            {
                deltaHueAngle -= 360;
            }
            else if (deltaHueAngle < -180)
            {
                deltaHueAngle += 360;
            }
        }

        var deltaH = 2 * Math.Sqrt(chromaProduct) * Math.Sin(deltaHueAngle * DegreesToRadians / 2);

        // Equations 12 to 22: the weighting functions, from the means
        var meanL = (reference.L + sample.L) / 2;
        var meanC = (c1 + c2) / 2;
        double meanH;
        if (chromaProduct == 0)
        {
            meanH = h1 + h2;
        }
        else if (Math.Abs(h1 - h2) <= 180)
        {
            meanH = (h1 + h2) / 2;
        }
        else if (h1 + h2 < 360)
        {
            meanH = (h1 + h2 + 360) / 2;
        }
        else
        {
            meanH = (h1 + h2 - 360) / 2;
        }

        var t = 1
            - (0.17 * Math.Cos((meanH - 30) * DegreesToRadians))
            + (0.24 * Math.Cos(2 * meanH * DegreesToRadians))
            + (0.32 * Math.Cos(((3 * meanH) + 6) * DegreesToRadians))
            - (0.20 * Math.Cos(((4 * meanH) - 63) * DegreesToRadians));
        var hueOffset = (meanH - 275) / 25;
        var deltaTheta = 30 * Math.Exp(-(hueOffset * hueOffset));
        var meanC7 = Math.Pow(meanC, 7);
        var rotationChroma = 2 * Math.Sqrt(meanC7 / (meanC7 + Pow25To7));
        var lightnessOffset = (meanL - 50) * (meanL - 50);
        var lightnessScale = 1 + (0.015 * lightnessOffset / Math.Sqrt(20 + lightnessOffset));
        var chromaScale = 1 + (0.045 * meanC);
        var hueScale = 1 + (0.015 * meanC * t);
        var rotation = -Math.Sin(2 * deltaTheta * DegreesToRadians) * rotationChroma;

        var lightnessTerm = deltaL / (lightnessWeight * lightnessScale);
        var chromaTerm = deltaC / (chromaWeight * chromaScale);
        var hueTerm = deltaH / (hueWeight * hueScale);
        return Math.Sqrt((lightnessTerm * lightnessTerm) + (chromaTerm * chromaTerm) + (hueTerm * hueTerm) + (rotation * chromaTerm * hueTerm));

        // Equation 4: the hue in degrees in [0, 360), 0 for a neutral color
        static double Hue(double b, double a)
        {
            if (a == 0 && b == 0)
                return 0;

            var hue = Math.Atan2(b, a) / DegreesToRadians;
            return hue < 0 ? hue + 360 : hue;
        }
    }

    /// <summary>Computes the Oklab color difference (ΔEOK, CSS Color 4 section 13.1.2): the Euclidean distance in Oklab.</summary>
    /// <param name="reference">The first color.</param>
    /// <param name="sample">The second color.</param>
    /// <returns>The difference; 0.02 is about a just noticeable difference.</returns>
    public static double DeltaEOK(Oklab reference, Oklab sample)
    {
        var deltaL = reference.L - sample.L;
        var deltaA = reference.A - sample.A;
        var deltaB = reference.B - sample.B;
        return Math.Sqrt((deltaL * deltaL) + (deltaA * deltaA) + (deltaB * deltaB));
    }
}
