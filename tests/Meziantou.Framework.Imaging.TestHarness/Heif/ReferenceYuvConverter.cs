using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.Heif;

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
/// 5 (BT.470BG), 6 (BT.601), 7 (SMPTE 240M), 8 (YCgCo), 9 (BT.2020 non-constant luminance) and 2 (unspecified, converted
/// as BT.601).
/// <para>
/// The converter is also the oracle of the HEIC pixel references: the overloads that take the position of the chroma
/// samples along each axis convert HEVC planes, whose positions <see cref="GetHevcChromaSiting"/> gives (Rec. ITU-T H.265,
/// clause 6.2 and figure E.1). They include the position that AV1 cannot declare: the chroma samples of a 4:2:2 picture
/// that are co-sited with the luma samples of the even columns.
/// </para>
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
    /// <returns><see langword="true"/> for 0, 1, 2 and 4 to 9.</returns>
    public static bool IsSupportedMatrix(int matrixCoefficients) => matrixCoefficients is 0 or 1 or 2 or (>= 4 and <= 9);

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
    /// <returns>Three samples per pixel, rows in order.</returns>
    /// <exception cref="NotSupportedException">The matrix coefficients are not implemented.</exception>
    /// <exception cref="ArgumentException">The identity matrix is used with subsampled chroma, or a plane does not have its size.</exception>
    public static int[] ConvertToRgb(ReadOnlySpan<ushort> y, ReadOnlySpan<ushort> u, ReadOnlySpan<ushort> v, int width, int height, int bitDepth, int subsamplingX, int subsamplingY, int chromaSamplePosition, int matrixCoefficients, bool fullRange, int outputMaximum)
    {
        var (cositedHorizontally, cositedVertically) = GetAv1ChromaSiting(subsamplingX, subsamplingY, chromaSamplePosition);
        return ConvertToRgb(y, u, v, width, height, bitDepth, subsamplingX, subsamplingY, cositedHorizontally, cositedVertically, matrixCoefficients, fullRange, outputMaximum);
    }

    /// <summary>Converts planes to interleaved R, G, B samples, with the position of the chroma samples given along each axis.</summary>
    /// <param name="y">The first plane: <paramref name="width"/> x <paramref name="height"/> samples.</param>
    /// <param name="u">The second plane: <c>(width + subsamplingX) >> subsamplingX</c> samples per row, <c>(height + subsamplingY) >> subsamplingY</c> rows.</param>
    /// <param name="v">The third plane, of the size of the second one.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="bitDepth">The bit depth of the planes: 8, 10 or 12.</param>
    /// <param name="subsamplingX">1 when the chroma planes have half the columns.</param>
    /// <param name="subsamplingY">1 when the chroma planes have half the rows.</param>
    /// <param name="chromaCositedHorizontally">Whether the chroma samples are on the luma columns of even index, instead of halfway to the next column. It only applies to planes with half the columns.</param>
    /// <param name="chromaCositedVertically">Whether the chroma samples are on the luma rows of even index, instead of halfway to the next row. It only applies to planes with half the rows.</param>
    /// <param name="matrixCoefficients">The matrix coefficients that apply to the image.</param>
    /// <param name="fullRange">Whether the samples use the full range.</param>
    /// <param name="outputMaximum">The maximum of the output samples: 255 or 65535.</param>
    /// <returns>Three samples per pixel, rows in order.</returns>
    /// <exception cref="NotSupportedException">The matrix coefficients are not implemented.</exception>
    /// <exception cref="ArgumentException">The identity matrix is used with subsampled chroma, or a plane does not have its size.</exception>
    public static int[] ConvertToRgb(ReadOnlySpan<ushort> y, ReadOnlySpan<ushort> u, ReadOnlySpan<ushort> v, int width, int height, int bitDepth, int subsamplingX, int subsamplingY, bool chromaCositedHorizontally, bool chromaCositedVertically, int matrixCoefficients, bool fullRange, int outputMaximum)
    {
        if (!IsSupportedMatrix(matrixCoefficients))
            throw new NotSupportedException($"The matrix coefficients {matrixCoefficients.ToString(CultureInfo.InvariantCulture)} are not implemented.");

        if (matrixCoefficients == 0 && (subsamplingX != 0 || subsamplingY != 0))
            throw new ArgumentException("The identity matrix requires 4:4:4 samples.", nameof(matrixCoefficients));

        if (y.Length != width * height)
            throw new ArgumentException("The luma plane does not have the size of the image.", nameof(y));

        var fullU = UpsampleChroma(u, width, height, subsamplingX, subsamplingY, chromaCositedHorizontally, chromaCositedVertically);
        var fullV = UpsampleChroma(v, width, height, subsamplingX, subsamplingY, chromaCositedHorizontally, chromaCositedVertically);
        var result = new int[3 * width * height];
        var maximum = (1 << bitDepth) - 1;
        var scale = 1 << (bitDepth - 8);
        BigInteger lumaOffset = fullRange ? 0 : 16 * scale;
        BigInteger lumaRange = fullRange ? maximum : 219 * scale;
        BigInteger chromaOffset = 1 << (bitDepth - 1);
        BigInteger chromaRange = fullRange ? maximum : 224 * scale;

        // KR and KB as fractions over 10000 (ITU-T H.273, table 4)
        const int Denominator = 10000;
        var (kr, kb) = matrixCoefficients switch
        {
            1 => (2126, 722),
            4 => (3000, 1100),
            2 or 5 or 6 => (2990, 1140),
            7 => (2120, 870),
            9 => (2627, 593),
            _ => (0, 0),
        };
        BigInteger kg = Denominator - kr - kb;

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
            if (matrixCoefficients == 8)
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
        var (cositedHorizontally, cositedVertically) = GetAv1ChromaSiting(subsamplingX, subsamplingY, chromaSamplePosition);
        return UpsampleChroma(plane, width, height, subsamplingX, subsamplingY, cositedHorizontally, cositedVertically);
    }

    /// <summary>
    /// Gets the position of the chroma samples of an HEVC picture (Rec. ITU-T H.265): the chroma samples of a 4:2:2 picture
    /// are co-sited with the luma samples of the even columns (clause 6.2, figure 6-2); those of a 4:2:0 picture are at the
    /// position that <c>chroma_sample_loc_type_top_field</c> gives (figure E.1), which is 0 when the bitstream does not
    /// signal it (clause E.3.1): on the luma columns of even index and halfway between two rows (0), in the middle of four
    /// luma samples (1), on a luma sample (2), or halfway between two columns on a luma row (3).
    /// </summary>
    /// <param name="chromaFormatIdc">The <c>chroma_format_idc</c> of the sequence parameter set: 0 (monochrome), 1 (4:2:0), 2 (4:2:2) or 3 (4:4:4).</param>
    /// <param name="chromaSampleLocType">The <c>chroma_sample_loc_type_top_field</c> of the video usability information, or 0 when it is absent.</param>
    /// <returns>Whether the chroma samples are co-sited with luma samples along each axis; <see langword="false"/> along an axis that is not subsampled.</returns>
    /// <exception cref="NotSupportedException">The type is 4 or 5: the chroma samples are on the luma rows of odd index.</exception>
    public static (bool CositedHorizontally, bool CositedVertically) GetHevcChromaSiting(int chromaFormatIdc, int chromaSampleLocType) => (chromaFormatIdc, chromaSampleLocType) switch
    {
        (2, _) => (true, false),
        (1, 0) => (true, false),
        (1, 1) => (false, false),
        (1, 2) => (true, true),
        (1, 3) => (false, true),
        (1, _) => throw new NotSupportedException($"The chroma sample location type {chromaSampleLocType.ToString(CultureInfo.InvariantCulture)} is not implemented."),
        _ => (false, false),
    };

    /// <summary>Upsamples a chroma plane to the resolution of the luma plane, with the position of its samples given along each axis.</summary>
    /// <param name="plane">The chroma plane.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="subsamplingX">1 when the plane has half the columns.</param>
    /// <param name="subsamplingY">1 when the plane has half the rows.</param>
    /// <param name="cositedHorizontally">Whether the chroma samples are on the luma columns of even index, instead of halfway to the next column.</param>
    /// <param name="cositedVertically">Whether the chroma samples are on the luma rows of even index, instead of halfway to the next row.</param>
    /// <returns><paramref name="width"/> x <paramref name="height"/> samples.</returns>
    /// <exception cref="ArgumentException">The plane does not have the size of a chroma plane of the image.</exception>
    public static ushort[] UpsampleChroma(ReadOnlySpan<ushort> plane, int width, int height, int subsamplingX, int subsamplingY, bool cositedHorizontally, bool cositedVertically)
    {
        var chromaWidth = (width + subsamplingX) >> subsamplingX;
        var chromaHeight = (height + subsamplingY) >> subsamplingY;
        if (plane.Length != chromaWidth * chromaHeight)
            throw new ArgumentException("The chroma plane does not have the size that the subsampling gives it.", nameof(plane));

        var result = new ushort[width * height];
        for (var row = 0; row < height; row++)
        {
            var (firstRow, secondRow, secondRowWeight) = Taps(row, chromaHeight, subsamplingY, centered: !cositedVertically);
            for (var column = 0; column < width; column++)
            {
                var (firstColumn, secondColumn, secondColumnWeight) = Taps(column, chromaWidth, subsamplingX, centered: !cositedHorizontally);

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

    // The position that an AV1 sequence header declares only applies to 4:2:0 planes: the chroma samples of the other
    // subsampled formats are centered
    private static (bool CositedHorizontally, bool CositedVertically) GetAv1ChromaSiting(int subsamplingX, int subsamplingY, int chromaSamplePosition)
    {
        var position = subsamplingX == 1 && subsamplingY == 1 ? chromaSamplePosition : ChromaUnknown;
        return (position is ChromaColocated or ChromaVertical, position == ChromaColocated);
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
    private static int RoundClipped(BigInteger numerator, BigInteger denominator, int maximum)
    {
        if (numerator.Sign <= 0)
            return 0;

        if (numerator >= denominator)
            return maximum;

        return (int)BigInteger.Divide((2 * numerator * maximum) + denominator, 2 * denominator);
    }
}
