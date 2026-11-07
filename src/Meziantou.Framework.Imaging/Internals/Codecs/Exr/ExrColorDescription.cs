using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color description of OpenEXR samples: linear light whose primaries and white point are the <c>chromaticities</c>
/// attribute, or those of Rec. ITU-R BT.709 (sRGB) and D65 without it (OpenEXR Technical Introduction, "RGB Color").
/// </summary>
/// <remarks>
/// The label is the one a decoder gives (never applied): the primaries of BT.709 are
/// <see cref="ColorTransferFunction.Linear"/>, those that ITU-T H.273 enumerates (BT.2020, DCI-P3, Display P3) are a CICP
/// description with the linear transfer characteristics (8), and the others an ICC profile written for them, with linear
/// tone curves. The writer stores the same descriptions in the attribute.
/// </remarks>
internal static class ExrColorDescription
{
    /// <summary>The chromaticities of BT.709 and D65, as floats: the default of an OpenEXR file.</summary>
    internal static ReadOnlySpan<float> Bt709 => [0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, 0.3127f, 0.3290f];

    private static ReadOnlySpan<float> Bt2020 => [0.708f, 0.292f, 0.170f, 0.797f, 0.131f, 0.046f, 0.3127f, 0.3290f];

    private static ReadOnlySpan<float> DisplayP3 => [0.680f, 0.320f, 0.265f, 0.690f, 0.150f, 0.060f, 0.3127f, 0.3290f];

    private static ReadOnlySpan<float> DciP3 => [0.680f, 0.320f, 0.265f, 0.690f, 0.150f, 0.060f, 0.314f, 0.351f];

    /// <summary>Labels the metadata of a decoded image.</summary>
    public static void Apply(ExrHeader header, ImageMetadata metadata)
    {
        var chromaticities = header.Chromaticities ?? Bt709;
        if (chromaticities.SequenceEqual(Bt709))
        {
            metadata.TransferFunction = ColorTransferFunction.Linear;
            return;
        }

        var primaries = GetCicpPrimaries(chromaticities);
        if (primaries != 0)
        {
            metadata.Cicp = new CicpColorDescription(primaries, transferCharacteristics: 8, matrixCoefficients: 0, fullRange: true);
            return;
        }

        Span<double> values = stackalloc double[6];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = chromaticities[i];
        }

        metadata.IccProfile = BuiltInIccProfiles.TryCreate(gray: false, values, chromaticities[6], chromaticities[7], BuiltInIccCurve.Linear, exponent: 1, renderingIntent: 0, "OpenEXR chromaticities, linear")
            ?? throw ExrFormat.Invalid("the chromaticities attribute does not describe valid primaries and white point.");
    }

    /// <summary>Gets the code point of ITU-T H.273 of chromaticities, or 0 when it has none (BT.709 is 1).</summary>
    public static byte GetCicpPrimaries(ReadOnlySpan<float> chromaticities)
    {
        if (chromaticities.SequenceEqual(Bt709))
            return 1;

        if (chromaticities.SequenceEqual(Bt2020))
            return 9;

        if (chromaticities.SequenceEqual(DciP3))
            return 11;

        if (chromaticities.SequenceEqual(DisplayP3))
            return 12;

        return 0;
    }

    /// <summary>Gets the chromaticities of a code point of ITU-T H.273 that <see cref="GetCicpPrimaries"/> recognizes.</summary>
    public static ReadOnlySpan<float> GetChromaticities(byte primaries) => primaries switch
    {
        1 => Bt709,
        9 => Bt2020,
        11 => DciP3,
        12 => DisplayP3,
        _ => throw new ArgumentOutOfRangeException(nameof(primaries), primaries, "The primaries have no chromaticities."),
    };
}
