using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color description of Radiance HDR samples: linear light whose primaries and white point are the <c>PRIMARIES</c>
/// variable, or the standard primaries of Radiance without it.
/// </summary>
/// <remarks>
/// <para>
/// The label is the one a decoder gives (never applied), and the one OpenEXR chromaticities get. The standard primaries of
/// Radiance are read as those of BT.709 (sRGB) and D65, labeled <see cref="ColorTransferFunction.Linear"/>, as the other
/// readers of the format read them: they share the red and the blue primaries of BT.709, and differ by 0.01 in the x of
/// green and by their white point (the equal-energy white E). Explicit primaries that are those of BT.709 are labeled the same way, those that ITU-T H.273
/// enumerates (BT.2020, DCI-P3, Display P3) are a CICP description with the linear transfer characteristics (8), and the
/// others an ICC profile written for them, with linear tone curves.
/// </para>
/// <para>
/// The values of the header are decimal text: they are compared in single precision, as OpenEXR stores chromaticities
/// (so <c>0.64</c> and <c>0.6400</c> are the same value).
/// </para>
/// </remarks>
internal static class RadianceColorDescription
{
    /// <summary>Labels the metadata of a decoded image.</summary>
    /// <param name="primaries">The eight values of the <c>PRIMARIES</c> variable, or <see langword="null"/>.</param>
    /// <param name="metadata">The metadata to label.</param>
    public static void Apply(double[]? primaries, ImageMetadata metadata)
    {
        if (primaries is null || IsStandard(primaries))
        {
            metadata.TransferFunction = ColorTransferFunction.Linear;
            return;
        }

        Span<float> chromaticities = stackalloc float[8];
        for (var i = 0; i < chromaticities.Length; i++)
        {
            chromaticities[i] = (float)primaries[i];
        }

        var cicp = ExrColorDescription.GetCicpPrimaries(chromaticities);
        if (cicp == 1)
        {
            metadata.TransferFunction = ColorTransferFunction.Linear;
            return;
        }

        if (cicp != 0)
        {
            metadata.Cicp = new CicpColorDescription(cicp, transferCharacteristics: 8, matrixCoefficients: 0, fullRange: true);
            return;
        }

        metadata.IccProfile = CreateProfile(primaries) ?? throw new InvalidOperationException("The primaries were not validated.");
    }

    /// <summary>Determines whether eight values describe primaries and a white point an ICC profile can be written for.</summary>
    public static bool IsValid(double[] primaries) => IsStandard(primaries) || CreateProfile(primaries) is not null;

    private static IccProfile? CreateProfile(double[] primaries)
        => BuiltInIccProfiles.TryCreate(gray: false, primaries.AsSpan(0, 6), primaries[6], primaries[7], BuiltInIccCurve.Linear, exponent: 1, renderingIntent: 0, "Radiance primaries, linear");

    /// <summary>Gets the chromaticities the encoder writes for a CICP code point of primaries (1 for an image labeled linear).</summary>
    public static ReadOnlySpan<float> GetChromaticities(byte primaries) => ExrColorDescription.GetChromaticities(primaries);

    private static bool IsStandard(ReadOnlySpan<double> primaries)
    {
        var standard = RadianceFormat.StandardPrimaries;
        for (var i = 0; i < standard.Length; i++)
        {
            if ((float)primaries[i] != (float)standard[i])
                return false;
        }

        return true;
    }
}
