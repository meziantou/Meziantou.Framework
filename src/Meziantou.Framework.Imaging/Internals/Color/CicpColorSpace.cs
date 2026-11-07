using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color space that a CICP color description (Rec. ITU-T H.273) gives to stored samples, as the source of a color
/// conversion: the transfer characteristics, whose function decodes the samples to linear light, and the color
/// primaries, whose chromaticities relate that light to CIEXYZ. The matrix coefficients and the range of the description
/// are not part of it: they describe how the samples were stored in a file, and the samples of an image are R'G'B' or
/// gray in full range.
/// </summary>
/// <param name="Primaries">The color primaries code point, which has chromaticities (<see cref="CicpPrimaries.IsSupported"/>); not used for gray samples.</param>
/// <param name="Transfer">The transfer characteristics code point, which has a function (<see cref="CicpTransfer.IsSupported"/>).</param>
/// <param name="IsGray">Whether the samples are gray: one channel, the fraction of the white of the color space.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CicpColorSpace(byte Primaries, byte Transfer, bool IsGray)
{
    /// <summary>Gets the number of samples per color: 1 (gray) or 3 (red, green, blue).</summary>
    public int ChannelCount => IsGray ? 1 : 3;

    /// <summary>
    /// Computes the matrix from the linear red, green and blue samples to CIEXYZ of the ICC profile connection space: the
    /// primaries and the white point give CIEXYZ relative to that white, which the linear Bradford transform adapts to
    /// the D50 illuminant of the connection space (ICC.1:2022, annex E), as the colorants of a display profile are.
    /// </summary>
    /// <param name="matrix">Receives the nine values, row by row.</param>
    /// <returns><see langword="false"/> when the chromaticities do not define a color space.</returns>
    public bool TryGetToConnectionSpace(Span<double> matrix)
    {
        Span<double> primaries = stackalloc double[6];
        if (!CicpPrimaries.TryGet(Primaries, primaries, out var whiteX, out var whiteY))
            return false;

        Span<double> rgbToXyz = stackalloc double[9];
        if (Primaries is 10)
        {
            // The primaries are CIEXYZ itself (SMPTE ST 428-1), whose equal-energy white is (1/3, 1/3): the samples are X, Y and Z
            rgbToXyz.Clear();
            rgbToXyz[0] = rgbToXyz[4] = rgbToXyz[8] = 1;
        }
        else if (!ChromaticityMath.TryGetRgbToXyz(primaries, whiteX, whiteY, rgbToXyz))
        {
            return false;
        }

        // The cone responses are scaled by the ratio of the responses to the two whites. The inverse of the cone response
        // matrix is computed, not taken from its published seven digits, whose rounding would be visible in 16-bit samples
        ReadOnlySpan<double> bradford = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];
        Span<double> inverse = stackalloc double[9];
        if (!ChromaticityMath.TryInvert(bradford, inverse))
            return false;

        ReadOnlySpan<double> white = [whiteX / whiteY, 1, (1 - whiteX - whiteY) / whiteY];
        ReadOnlySpan<double> target = [IccColorimetry.D50X, IccColorimetry.D50Y, IccColorimetry.D50Z];
        Span<double> scaled = stackalloc double[9];
        for (var row = 0; row < 3; row++)
        {
            var cone = (bradford[row * 3] * white[0]) + (bradford[(row * 3) + 1] * white[1]) + (bradford[(row * 3) + 2] * white[2]);
            var targetCone = (bradford[row * 3] * target[0]) + (bradford[(row * 3) + 1] * target[1]) + (bradford[(row * 3) + 2] * target[2]);
            if (cone is 0 || !double.IsFinite(targetCone / cone))
                return false;

            for (var column = 0; column < 3; column++)
            {
                scaled[(row * 3) + column] = bradford[(row * 3) + column] * (targetCone / cone);
            }
        }

        Span<double> adaptation = stackalloc double[9];
        ChromaticityMath.Multiply(inverse, scaled, adaptation);
        ChromaticityMath.Multiply(adaptation, rgbToXyz, matrix);
        return true;
    }
}
