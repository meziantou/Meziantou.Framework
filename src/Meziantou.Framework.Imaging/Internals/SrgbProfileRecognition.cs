using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Recognizes ICC profiles that describe sRGB (or sGray) pixels, for <see cref="ResizeWorkingSpace.LinearSrgb"/>
///. No ICC transform is applied by these operations: a profile is accepted only when its matrix/TRC tags
/// already describe the IEC 61966-2-1 encoding, so that linearizing with the sRGB transfer function is correct.
/// </summary>
/// <remarks>
/// <para>An RGB profile is recognized when its PCS is XYZ, its <c>rXYZ</c>/<c>gXYZ</c>/<c>bXYZ</c> colorants equal the
/// D50-adapted (Bradford) sRGB primaries within <see cref="ColorantTolerance"/>, and its <c>rTRC</c>/<c>gTRC</c>/<c>bTRC</c>
/// curves match the sRGB transfer function within <see cref="CurveTolerance"/>. A gray profile is recognized when its
/// <c>kTRC</c> matches. Curves may be sampled (<c>curv</c> with at least two entries, compared at every entry) or
/// parametric (<c>para</c> types 0 to 4, compared at 1,025 evenly spaced inputs). Pure gamma curves (such as 2.2), identity
/// curves, LUT-only profiles and anything malformed or incomplete are not recognized.</para>
/// </remarks>
internal static class SrgbProfileRecognition
{
    /// <summary>The maximum difference of each colorant component (XYZ, D50).</summary>
    public const double ColorantTolerance = 0.002;

    /// <summary>The maximum difference between a tone curve and the sRGB decoding function (normalized output).</summary>
    public const double CurveTolerance = 0.002;

    private const int ParametricSamples = 1024;

    // sRGB primaries adapted from D65 to D50 with the Bradford transform (as published with the ICC sRGB profiles)
    private static readonly (uint Signature, double X, double Y, double Z)[] SrgbColorants =
    [
        (IccReader.TagRedColorant, 0.4360747, 0.2225045, 0.0139322),
        (IccReader.TagGreenColorant, 0.3850649, 0.7168786, 0.0971045),
        (IccReader.TagBlueColorant, 0.1430804, 0.0606169, 0.7141733),
    ];

    /// <summary>Determines whether pixels of <paramref name="format"/> labeled with <paramref name="profile"/> are sRGB (or sGray) encoded.</summary>
    public static bool IsSrgb(IccProfile profile, PixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var data = profile.Data.Span;
        if (data.Length < IccReader.HeaderSize + 4 || !ColorProfileCompatibility.IsCompatible(profile, format))
            return false;

        if (PixelFormats.IsGrayscale(format))
            return IsSrgbCurve(data, IccReader.TagGrayCurve);

        if (!data.Slice(20, 4).SequenceEqual("XYZ "u8))
            return false;

        foreach (var (signature, x, y, z) in SrgbColorants)
        {
            if (!IccReader.TryGetTag(data, signature, out var tag) || tag.Length < 20 || !tag[..4].SequenceEqual("XYZ "u8))
                return false;

            if (Math.Abs(IccReader.ReadS15Fixed16(tag[8..]) - x) > ColorantTolerance
                || Math.Abs(IccReader.ReadS15Fixed16(tag[12..]) - y) > ColorantTolerance
                || Math.Abs(IccReader.ReadS15Fixed16(tag[16..]) - z) > ColorantTolerance)
                return false;
        }

        return IsSrgbCurve(data, IccReader.TagRedCurve) && IsSrgbCurve(data, IccReader.TagGreenCurve) && IsSrgbCurve(data, IccReader.TagBlueCurve);
    }

    private static bool IsSrgbCurve(ReadOnlySpan<byte> data, uint signature)
    {
        if (!IccReader.TryGetTag(data, signature, out var tag) || tag.Length < 12)
            return false;

        if (tag[..4].SequenceEqual("curv"u8))
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
            if (count < 2 || count > (tag.Length - 12) / 2)
                return false;

            for (var i = 0; i < count; i++)
            {
                var value = BinaryPrimitives.ReadUInt16BigEndian(tag[(12 + (2 * i))..]) / 65535.0;
                if (Math.Abs(value - SrgbTransfer.Decode((double)i / (count - 1))) > CurveTolerance)
                    return false;
            }

            return true;
        }

        if (tag[..4].SequenceEqual("para"u8))
        {
            var function = BinaryPrimitives.ReadUInt16BigEndian(tag[8..]);
            var parameterCount = IccCurve.GetParameterCount(function);
            if (parameterCount == 0 || tag.Length < 12 + (4 * parameterCount))
                return false;

            Span<double> p = stackalloc double[7];
            for (var i = 0; i < parameterCount; i++)
            {
                p[i] = IccReader.ReadS15Fixed16(tag[(12 + (4 * i))..]);
            }

            for (var i = 0; i <= ParametricSamples; i++)
            {
                var x = (double)i / ParametricSamples;
                if (!(Math.Abs(IccCurve.EvaluateParametric(function, p, x) - SrgbTransfer.Decode(x)) <= CurveTolerance))
                    return false;
            }

            return true;
        }

        return false;
    }
}
