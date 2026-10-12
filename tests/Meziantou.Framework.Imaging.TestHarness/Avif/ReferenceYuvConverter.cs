using System.Numerics;
using Meziantou.Framework.Imaging.TestHarness.Color;

namespace Meziantou.Framework.Imaging.TestHarness.Avif;

/// <summary>
/// The oracle of the AVIF pixel references: converts decoded AV1 planes to R'G'B' with the equations of ITU-T H.273
/// (section 8.3 "Matrix coefficients") in exact rational arithmetic, without any floating-point number. It never uses the
/// library under test; it follows the conversion that the library documents:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Subsampled chroma is upsampled to the resolution of the luma plane, at the precision of the planes, with a triangle
/// (bilinear) filter at the sample position the bitstream declares: centered between the luma samples (weights 9, 3, 3, 1
/// over 16 for 4:2:0; 3, 1 over 4 for 4:2:2), vertical (<c>chroma_sample_position</c> 1: on the luma columns, between the
/// rows) or co-located (2: on the top-left luma sample). The samples of the edges are replicated. An upsampled value that
/// falls between two integers is rounded to the nearest one, halves up.
/// </description></item>
/// <item><description>
/// The matrix is applied to the normalized samples with the exact K<sub>R</sub> and K<sub>B</sub> of table 4, and the range
/// (full, or limited: 16 to 235 and 16 to 240 scaled to the bit depth) is expanded to the range of the output. Each
/// component is clipped, then rounded once to the nearest integer, halves up.
/// </description></item>
/// <item><description>
/// The samples that no matrix applies to (identity matrix, monochrome, alpha) are scaled from their range to the range of
/// the output and rounded to the nearest integer.
/// </description></item>
/// </list>
/// The library computes the matrix with coefficients rounded to 16 fractional bits: its result differs from this one by at
/// most one unit, and only for matrixed content. Supported matrix coefficients: 0 (identity), 1 (BT.709), 4 (FCC),
/// 5 (BT.470BG), 6 (BT.601), 7 (SMPTE 240M), 8 (YCgCo), 9 (BT.2020 non-constant luminance), 2 (unspecified, converted
/// as BT.601), 11 (SMPTE ST 2085, whose two constants are the decimal numbers of the recommendation) and 12
/// (chromaticity-derived non-constant luminance: K<sub>R</sub> and K<sub>B</sub> are the luminance of the red and blue
/// primaries, computed here as fractions by inverting the matrix of the chromaticities of table 2, which is another
/// route than the two closed-form equations of section 8.3). The constant luminance coefficients (10: BT.2020; 13:
/// chromaticity-derived) and ICtCp (14, for the PQ and HLG transfer characteristics) are not a matrix of the coded
/// samples: they go through the transfer characteristics function, which <see cref="ReferenceCicpTransfer"/> evaluates
/// in <see cref="decimal"/> arithmetic, and the matrices of ICtCp are inverted as fractions.
/// </remarks>
public static class ReferenceYuvConverter
{
    /// <summary>The <c>chroma_sample_position</c> of samples whose position is unknown: they are converted as centered samples.</summary>
    public const int ChromaUnknown = 0;

    /// <summary>The <c>chroma_sample_position</c> of samples on the luma columns, between the luma rows.</summary>
    public const int ChromaVertical = 1;

    /// <summary>The <c>chroma_sample_position</c> of samples co-located with the top-left luma sample.</summary>
    public const int ChromaColocated = 2;

    /// <summary>Gets a value indicating whether the converter implements matrix coefficients.</summary>
    /// <param name="matrixCoefficients">The code point of ITU-T H.273.</param>
    /// <returns><see langword="true"/> for 0, 1, 2 and 4 to 14.</returns>
    public static bool IsSupportedMatrix(int matrixCoefficients) => matrixCoefficients is 0 or 1 or 2 or (>= 4 and <= 14);

    /// <summary>
    /// Computes the K<sub>R</sub> and K<sub>B</sub> of color primaries (ITU-T H.273, table 2) as exact fractions of one
    /// denominator: the luminance of the red and of the blue primary for a white of luminance 1, which is the second row of
    /// the matrix from linear R, G, B to CIE X, Y, Z.
    /// </summary>
    /// <param name="colorPrimaries">The color primaries code point.</param>
    /// <returns>The numerators of K<sub>R</sub> and K<sub>B</sub> and their denominator, or <see langword="null"/> for primaries without chromaticities.</returns>
    public static (BigInteger Kr, BigInteger Kb, BigInteger Denominator)? GetLuminanceWeights(int colorPrimaries)
    {
        // The x and y of red, green, blue and white, in thousandths (ten-thousandths for the white D65)
        (int[] Primaries, int[] White)? chromaticities = colorPrimaries switch
        {
            1 => ([640, 330, 300, 600, 150, 60], [3127, 3290]),
            4 => ([670, 330, 210, 710, 140, 80], [3100, 3160]),
            5 => ([640, 330, 290, 600, 150, 60], [3127, 3290]),
            6 or 7 => ([630, 340, 310, 595, 155, 70], [3127, 3290]),
            8 => ([681, 319, 243, 692, 145, 49], [3100, 3160]),
            9 => ([708, 292, 170, 797, 131, 46], [3127, 3290]),
            10 => ([1000, 0, 0, 1000, 0, 0], [3333, 3333]),
            11 => ([680, 320, 265, 690, 150, 60], [3140, 3510]),
            12 => ([680, 320, 265, 690, 150, 60], [3127, 3290]),
            22 => ([630, 340, 295, 605, 155, 77], [3127, 3290]),
            _ => null,
        };

        if (chromaticities is not var (p, w))
            return null;

        // Columns of the matrix M: the (x, y, z) of each primary, over 1000; the white is (xW, yW, zW) / yW
        BigInteger[] m =
        [
            p[0], p[2], p[4],
            p[1], p[3], p[5],
            1000 - p[0] - p[1], 1000 - p[2] - p[3], 1000 - p[4] - p[5],
        ];
        BigInteger xW = w[0], yW = w[1];

        // The equal-energy white of the CIE XYZ primaries is one third exactly
        var zW = colorPrimaries == 10 ? yW : 10000 - xW - yW;

        // The scale of each primary is the solution S of M S = W (Cramer's rule): S_i = det(M with column i replaced by W) / det(M),
        // and its luminance is y_i S_i. The common factors (1000 per chromaticity, yW of the white) cancel in the fractions
        var determinant = Determinant(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
        var red = Determinant(xW, m[1], m[2], yW, m[4], m[5], zW, m[7], m[8]);
        var blue = Determinant(m[0], m[1], xW, m[3], m[4], yW, m[6], m[7], zW);
        var kr = m[3] * red;
        var kb = m[5] * blue;
        var denominator = determinant * yW;
        return denominator.Sign < 0 ? (-kr, -kb, -denominator) : (kr, kb, denominator);

        static BigInteger Determinant(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger e, BigInteger f, BigInteger g, BigInteger h, BigInteger i)
            => (a * ((e * i) - (f * h))) - (b * ((d * i) - (f * g))) + (c * ((d * h) - (e * g)));
    }

    /// <summary>Converts planes to interleaved R, G, B samples.</summary>
    /// <param name="y">The first plane: <paramref name="width"/> x <paramref name="height"/> samples.</param>
    /// <param name="u">The second plane: <c>(width + subsamplingX) >> subsamplingX</c> samples per row, <c>(height + subsamplingY) >> subsamplingY</c> rows.</param>
    /// <param name="v">The third plane, of the size of the second one.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="bitDepth">The bit depth of the planes: 8, 10 or 12.</param>
    /// <param name="subsamplingX">1 when the chroma planes have half the columns.</param>
    /// <param name="subsamplingY">1 when the chroma planes have half the rows.</param>
    /// <param name="chromaSamplePosition">The <c>chroma_sample_position</c> of the sequence header.</param>
    /// <param name="matrixCoefficients">The matrix coefficients that apply to the image.</param>
    /// <param name="fullRange">Whether the samples use the full range.</param>
    /// <param name="outputMaximum">The maximum of the output samples: 255 or 65535.</param>
    /// <param name="colorPrimaries">The color primaries that apply to the image, from which the chromaticity-derived matrix coefficients (12 and 13) are computed.</param>
    /// <param name="transferCharacteristics">The transfer characteristics that apply to the image, which the constant luminance (10 and 13) and ICtCp (14) matrix coefficients go through.</param>
    /// <returns>Three samples per pixel, rows in order.</returns>
    /// <exception cref="NotSupportedException">
    /// The matrix coefficients are not implemented, they are derived from primaries that have no chromaticities, or they
    /// need transfer characteristics that have no function (ICtCp: other ones than PQ and HLG).
    /// </exception>
    /// <exception cref="ArgumentException">The identity matrix is used with subsampled chroma, or a plane does not have its size.</exception>
    public static int[] ConvertToRgb(ReadOnlySpan<ushort> y, ReadOnlySpan<ushort> u, ReadOnlySpan<ushort> v, int width, int height, int bitDepth, int subsamplingX, int subsamplingY, int chromaSamplePosition, int matrixCoefficients, bool fullRange, int outputMaximum, int colorPrimaries = 2, int transferCharacteristics = 2)
    {
        if (!IsSupportedMatrix(matrixCoefficients))
            throw new NotSupportedException($"The matrix coefficients {matrixCoefficients.ToString(CultureInfo.InvariantCulture)} are not implemented.");

        if (matrixCoefficients is 12 or 13 && GetLuminanceWeights(colorPrimaries) is null)
            throw new NotSupportedException($"The color primaries {colorPrimaries.ToString(CultureInfo.InvariantCulture)} have no chromaticities to derive the matrix coefficients from.");

        if ((matrixCoefficients is 10 or 13 && !ReferenceCicpTransfer.IsSupported(transferCharacteristics)) || (matrixCoefficients == 14 && transferCharacteristics is not (16 or 18)))
            throw new NotSupportedException($"The matrix coefficients {matrixCoefficients.ToString(CultureInfo.InvariantCulture)} have no conversion with the transfer characteristics {transferCharacteristics.ToString(CultureInfo.InvariantCulture)}.");

        if (matrixCoefficients == 0 && (subsamplingX != 0 || subsamplingY != 0))
            throw new ArgumentException("The identity matrix requires 4:4:4 samples.", nameof(matrixCoefficients));

        if (y.Length != width * height)
            throw new ArgumentException("The luma plane does not have the size of the image.", nameof(y));

        var fullU = UpsampleChroma(u, width, height, subsamplingX, subsamplingY, chromaSamplePosition);
        var fullV = UpsampleChroma(v, width, height, subsamplingX, subsamplingY, chromaSamplePosition);
        var result = new int[3 * width * height];
        var maximum = (1 << bitDepth) - 1;
        var scale = 1 << (bitDepth - 8);
        BigInteger lumaOffset = fullRange ? 0 : 16 * scale;
        BigInteger lumaRange = fullRange ? maximum : 219 * scale;
        BigInteger chromaOffset = 1 << (bitDepth - 1);
        BigInteger chromaRange = fullRange ? maximum : 224 * scale;

        // KR and KB as fractions: over 10000 for the decimal numbers of ITU-T H.273, table 4, and the exact values of the
        // color primaries for the chromaticity-derived coefficients
        (BigInteger kr, BigInteger kb, BigInteger Denominator) = matrixCoefficients switch
        {
            1 => (2126, 722, 10000),
            4 => (3000, 1100, 10000),
            2 or 5 or 6 => (2990, 1140, 10000),
            7 => (2120, 870, 10000),
            9 => (2627, 593, 10000),
            10 => (2627, 593, 10000),
            12 or 13 => GetLuminanceWeights(colorPrimaries)!.Value,
            _ => (BigInteger.Zero, BigInteger.Zero, new BigInteger(10000)),
        };
        var kg = Denominator - kr - kb;

        for (var i = 0; i < width * height; i++)
        {
            if (matrixCoefficients == 0)
            {
                // The planes hold G, B and R (section 8.3, MatrixCoefficients equal to 0)
                result[(3 * i) + 1] = ScaleSample(y[i], bitDepth, fullRange, outputMaximum);
                result[(3 * i) + 2] = ScaleSample(fullU[i], bitDepth, fullRange, outputMaximum);
                result[3 * i] = ScaleSample(fullV[i], bitDepth, fullRange, outputMaximum);
                continue;
            }

            // E'Y = luma / lumaRange, E'PB = cb / chromaRange, E'PR = cr / chromaRange: every component is a fraction over
            // lumaRange * chromaRange * (the denominator of its coefficients)
            var luma = y[i] - lumaOffset;
            var cb = fullU[i] - chromaOffset;
            var cr = fullV[i] - chromaOffset;
            BigInteger red;
            BigInteger green;
            BigInteger blue;
            BigInteger denominator;
            if (matrixCoefficients is 10 or 13 or 14)
            {
                // The conversions that go through the transfer characteristics, on the normalized E'Y, E'PB and E'PR
                var normalized = matrixCoefficients == 14
                    ? ConvertIctcp((decimal)luma / (decimal)lumaRange, (decimal)cb / (decimal)chromaRange, (decimal)cr / (decimal)chromaRange, transferCharacteristics)
                    : ConvertConstantLuminance((decimal)luma / (decimal)lumaRange, (decimal)cb / (decimal)chromaRange, (decimal)cr / (decimal)chromaRange, (decimal)kr / (decimal)Denominator, (decimal)kb / (decimal)Denominator, transferCharacteristics);
                for (var channel = 0; channel < 3; channel++)
                {
                    result[(3 * i) + channel] = RoundClipped(normalized[channel], outputMaximum);
                }

                continue;
            }

            if (matrixCoefficients == 11)
            {
                // SMPTE ST 2085 (section 8.3, MatrixCoefficients equal to 11): E'Y = E'G, E'PB = (0.986566 E'B - E'Y) / 2 and
                // E'PR = (E'R - 0.991902 E'Y) / 2, hence G = Y, B = (Y + 2 PB) / 0.986566 and R = 0.991902 Y + 2 PR
                const int Million = 1_000_000;
                denominator = lumaRange * chromaRange * Million * 986_566;
                green = luma * chromaRange * Million * 986_566;
                blue = ((luma * chromaRange) + (2 * cb * lumaRange)) * Million * Million;
                red = ((991_902 * luma * chromaRange) + (2 * (BigInteger)Million * cr * lumaRange)) * 986_566;
            }
            else if (matrixCoefficients == 8)
            {
                // YCgCo: E'PB is Cg and E'PR is Co; G = Y + Cg, R = Y - Cg + Co, B = Y - Cg - Co
                denominator = lumaRange * chromaRange;
                var common = luma * chromaRange;
                green = common + (cb * lumaRange);
                red = common - (cb * lumaRange) + (cr * lumaRange);
                blue = common - (cb * lumaRange) - (cr * lumaRange);
            }
            else
            {
                // R = Y + 2 (1 - KR) PR, B = Y + 2 (1 - KB) PB, G = Y - (2 KB (1 - KB) PB + 2 KR (1 - KR) PR) / KG
                denominator = lumaRange * chromaRange * Denominator * kg;
                var common = luma * chromaRange * Denominator * kg;
                red = common + (2 * (Denominator - kr) * cr * lumaRange * kg);
                blue = common + (2 * (Denominator - kb) * cb * lumaRange * kg);
                green = common - (2 * kb * (Denominator - kb) * cb * lumaRange) - (2 * kr * (Denominator - kr) * cr * lumaRange);
            }

            result[3 * i] = RoundClipped(red, denominator, outputMaximum);
            result[(3 * i) + 1] = RoundClipped(green, denominator, outputMaximum);
            result[(3 * i) + 2] = RoundClipped(blue, denominator, outputMaximum);
        }

        return result;
    }

    /// <summary>Scales a sample that no matrix applies to (identity matrix, monochrome, alpha) from its range to the range of the output.</summary>
    /// <param name="sample">The sample.</param>
    /// <param name="bitDepth">The bit depth of the sample: 8, 10 or 12.</param>
    /// <param name="fullRange">Whether the sample uses the full range; otherwise 16 to 235, scaled to the bit depth.</param>
    /// <param name="outputMaximum">The maximum of the output: 255 or 65535.</param>
    /// <returns>The scaled sample, clipped and rounded to the nearest integer.</returns>
    public static int ScaleSample(int sample, int bitDepth, bool fullRange, int outputMaximum)
    {
        var scale = 1 << (bitDepth - 8);
        return fullRange
            ? RoundClipped(sample, (1 << bitDepth) - 1, outputMaximum)
            : RoundClipped(sample - (16 * scale), 219 * scale, outputMaximum);
    }

    /// <summary>
    /// Divides a color that is multiplied by the alpha of its pixel (premultiplied alpha) by that alpha: the exact quotient
    /// <c>color * outputMaximum / alpha</c>, rounded to the nearest integer (a half rounds up) and clipped to the output.
    /// Nothing is left of the color of a fully transparent pixel, which is black.
    /// </summary>
    /// <param name="color">The converted color sample, 0 to <paramref name="outputMaximum"/>.</param>
    /// <param name="alpha">The converted alpha sample, 0 to <paramref name="outputMaximum"/>.</param>
    /// <param name="outputMaximum">The maximum of the samples: 255 or 65535.</param>
    /// <returns>The color sample of straight alpha.</returns>
    public static int Unpremultiply(int color, int alpha, int outputMaximum)
    {
        if (alpha == 0)
            return 0;

        // floor(color * maximum / alpha + 1/2), in integers
        var quotient = ((2 * (BigInteger)color * outputMaximum) + alpha) / (2 * (BigInteger)alpha);
        return (int)BigInteger.Min(quotient, outputMaximum);
    }

    /// <summary>Upsamples a chroma plane to the resolution of the luma plane.</summary>
    /// <param name="plane">The chroma plane.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="subsamplingX">1 when the plane has half the columns.</param>
    /// <param name="subsamplingY">1 when the plane has half the rows.</param>
    /// <param name="chromaSamplePosition">The <c>chroma_sample_position</c> of the sequence header; it only applies to 4:2:0.</param>
    /// <returns><paramref name="width"/> x <paramref name="height"/> samples.</returns>
    /// <exception cref="ArgumentException">The plane does not have the size of a chroma plane of the image.</exception>
    public static ushort[] UpsampleChroma(ReadOnlySpan<ushort> plane, int width, int height, int subsamplingX, int subsamplingY, int chromaSamplePosition)
    {
        var chromaWidth = (width + subsamplingX) >> subsamplingX;
        var chromaHeight = (height + subsamplingY) >> subsamplingY;
        if (plane.Length != chromaWidth * chromaHeight)
            throw new ArgumentException("The chroma plane does not have the size that the subsampling gives it.", nameof(plane));

        var result = new ushort[width * height];
        var position = subsamplingX == 1 && subsamplingY == 1 ? chromaSamplePosition : ChromaUnknown;
        for (var row = 0; row < height; row++)
        {
            var (firstRow, secondRow, secondRowWeight) = Taps(row, chromaHeight, subsamplingY, centered: position != ChromaColocated);
            for (var column = 0; column < width; column++)
            {
                var (firstColumn, secondColumn, secondColumnWeight) = Taps(column, chromaWidth, subsamplingX, centered: position is not (ChromaColocated or ChromaVertical));

                // Weights in quarters along each axis: sixteenths in total
                var sum =
                    ((4 - secondRowWeight) * (4 - secondColumnWeight) * plane[(firstRow * chromaWidth) + firstColumn]) +
                    ((4 - secondRowWeight) * secondColumnWeight * plane[(firstRow * chromaWidth) + secondColumn]) +
                    (secondRowWeight * (4 - secondColumnWeight) * plane[(secondRow * chromaWidth) + firstColumn]) +
                    (secondRowWeight * secondColumnWeight * plane[(secondRow * chromaWidth) + secondColumn]);
                result[(row * width) + column] = (ushort)((sum + 8) / 16);
            }
        }

        return result;
    }

    // The two chroma samples around a luma position along one axis, and the weight of the second one in quarters.
    // Centered chroma sample i lies at the luma position 2i + 1/2: the luma sample 2i is a quarter of the way from i to
    // i - 1, the luma sample 2i + 1 a quarter of the way from i to i + 1. A co-located chroma sample i lies on the luma
    // sample 2i: the luma sample 2i + 1 is halfway to i + 1.
    private static (int First, int Second, int SecondWeight) Taps(int position, int chromaLength, int subsampling, bool centered)
    {
        if (subsampling == 0)
            return (position, position, 0);

        var index = position >> 1;
        var isOdd = (position & 1) == 1;
        if (centered)
            return (index, Math.Clamp(isOdd ? index + 1 : index - 1, 0, chromaLength - 1), 1);

        return isOdd ? (index, Math.Min(index + 1, chromaLength - 1), 2) : (index, index, 0);
    }

    // round(clip(numerator / denominator, 0, 1) * maximum), halves up; the denominator is positive
    // ITU-T H.273, section 8.3, MatrixCoefficients 10 and 13: E'Y = (KR R + KG G + KB B)', where the prime is the transfer
    // characteristics function and R, G and B are linear; E'PB = (E'B - E'Y) / (2 NB) when it is negative and
    // (E'B - E'Y) / (2 PB) otherwise, with NB = (1 - KB)' and PB = 1 - KB'; E'PR likewise with KR
    private static decimal[] ConvertConstantLuminance(decimal ey, decimal epb, decimal epr, decimal kr, decimal kb, int transfer)
    {
        var nb = ReferenceCicpTransfer.Encode(transfer, 1 - kb);
        var pb = 1 - ReferenceCicpTransfer.Encode(transfer, kb);
        var nr = ReferenceCicpTransfer.Encode(transfer, 1 - kr);
        var pr = 1 - ReferenceCicpTransfer.Encode(transfer, kr);
        var eb = ey + (2 * (epb < 0 ? nb : pb) * epb);
        var er = ey + (2 * (epr < 0 ? nr : pr) * epr);
        var luminance = ReferenceCicpTransfer.Decode(transfer, ey);
        var green = (luminance - (kr * ReferenceCicpTransfer.Decode(transfer, er)) - (kb * ReferenceCicpTransfer.Decode(transfer, eb))) / (1 - kr - kb);
        return [er, ReferenceCicpTransfer.Encode(transfer, green), eb];
    }

    // ITU-T H.273, section 8.3, MatrixCoefficients 14 (Rec. ITU-R BT.2100): L, M and S are linear combinations of the linear
    // R, G and B; I, CT and CP are linear combinations of L', M' and S'. The two matrices, over 4096, are inverted as
    // fractions (Cramer's rule) before any rounding
    private static decimal[] ConvertIctcp(decimal i, decimal ct, decimal cp, int transfer)
    {
        long[] rgbToLms = [1688, 2146, 262, 683, 2951, 462, 99, 309, 3688];
        long[] lmsToIctcp = transfer == 18
            ? [2048, 2048, 0, 3625, -7465, 3840, 9500, -9212, -288]
            : [2048, 2048, 0, 6610, -13613, 7003, 17933, -17390, -543];
        var encoded = Solve(lmsToIctcp, [i, ct, cp]);
        var lms = encoded.Select(value => ReferenceCicpTransfer.Decode(transfer, value)).ToArray();
        return [.. Solve(rgbToLms, lms).Select(value => ReferenceCicpTransfer.Encode(transfer, value))];

        // The solution x of (m / 4096) x = b
        static decimal[] Solve(long[] m, decimal[] b)
        {
            var determinant = Determinant(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
            return
            [
                4096 * Determinant(b[0], m[1], m[2], b[1], m[4], m[5], b[2], m[7], m[8]) / determinant,
                4096 * Determinant(m[0], b[0], m[2], m[3], b[1], m[5], m[6], b[2], m[8]) / determinant,
                4096 * Determinant(m[0], m[1], b[0], m[3], m[4], b[1], m[6], m[7], b[2]) / determinant,
            ];
        }

        static decimal Determinant(decimal a, decimal b, decimal c, decimal d, decimal e, decimal f, decimal g, decimal h, decimal k)
            => (a * ((e * k) - (f * h))) - (b * ((d * k) - (f * g))) + (c * ((d * h) - (e * g)));
    }

    // The nearest sample to a normalized value, which is clipped; a half rounds up
    private static int RoundClipped(decimal value, int maximum)
        => value <= 0 ? 0 : value >= 1 ? maximum : (int)Math.Floor((value * maximum) + 0.5m);

    private static int RoundClipped(BigInteger numerator, BigInteger denominator, int maximum)
    {
        if (numerator.Sign <= 0)
            return 0;

        if (numerator >= denominator)
            return maximum;

        return (int)BigInteger.Divide((2 * numerator * maximum) + denominator, 2 * denominator);
    }
}
