using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Computes the 64-bit difference hash (dHash) and perceptual hash (pHash) of frames, and the distance between two frames
/// according to each hash (<see cref="ImageSimilarity"/>).
/// </summary>
/// <remarks>
/// <para>
/// Both hashes start from a small luminance thumbnail (9×8 for dHash, 32×32 for pHash). Every source pixel contributes to
/// the thumbnail cells it overlaps in proportion to the overlapping area, so a change anywhere in the image affects the
/// thumbnail. The luminance uses the BT.601 weights and the thumbnail is computed with integer arithmetic, so it is exact:
/// uniform areas produce exactly equal cells, and the hashes do not depend on the hardware.
/// </para>
/// <para>
/// Differences smaller than a luminance level do not set bits: the dHash requires adjacent cells to differ by more than
/// half a level, and the pHash requires coefficients to exceed their median by more than one level. Otherwise the bits of
/// flat areas, whose cells or coefficients are all equal, would flip on invisible noise.
/// </para>
/// <para>
/// Hashes only describe the structure of an image, not its overall brightness, and transparent pixels have no luminance of
/// their own. The distances therefore add the difference between the mean luminances to the Hamming distance, and evaluate
/// frames that are not fully opaque composited over both a black and a white background, keeping the larger distance.
/// </para>
/// </remarks>
internal static class PerceptualHasher
{
    public const int MaxDistance = 64;

    private const int DHashWidth = 9;
    private const int DHashHeight = 8;
    private const int PHashSize = 32;
    private const int PHashLowFrequencySize = 8;

    // BT.601 luma weights, scaled to integers
    private const long RedWeight = 299;
    private const long GreenWeight = 587;
    private const long BlueWeight = 114;

    // The value of a white opaque pixel: the weights times 255 for the color, times 255 for the alpha
    private const long MaxPixelValue = (RedWeight + GreenWeight + BlueWeight) * 255 * 255;

    // One luminance level out of 255, in pixel values
    private const long LuminanceLevel = MaxPixelValue / 255;

    /// <summary>
    /// Coefficients that do not exceed the median by more than this are considered equal to it, in luminance levels. A
    /// change of <c>δ</c> levels in a single thumbnail cell moves every coefficient, and therefore their median, by at most
    /// <c>δ</c>. On a flat or one-directional image, where most coefficients equal the median, a bit can then only flip when
    /// the thumbnail cells change by more than half a luminance level in total, which is the tolerance the dHash uses
    /// between adjacent cells. Without it, the rounding noise of the DCT, or a single pixel changed by one level, decided
    /// about half the bits of such images.
    /// </summary>
    private const double PHashCoefficientTolerance = 1;

    private static readonly double[][] CosineTable = CreateCosineTable();

    /// <summary>Computes the dHash or the pHash of a frame composited over a black background.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static ulong ComputeHash(PixelSource source, bool perceptual, CancellationToken cancellationToken)
    {
        var (thumbnailWidth, thumbnailHeight) = GetThumbnailSize(perceptual);
        Span<long> thumbnail = stackalloc long[thumbnailWidth * thumbnailHeight];
        ComputeThumbnail(source, whiteBackground: false, thumbnailWidth, thumbnailHeight, thumbnail, cancellationToken);
        return perceptual
            ? ComputePHash(thumbnail, source.Width, source.Height)
            : ComputeDHash(thumbnail, source.Width, source.Height);
    }

    /// <summary>Computes the dHash or the pHash distance between two frames of the same size, from <c>0</c> to <see cref="MaxDistance"/>.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static int ComputeDistance(PixelSource expected, PixelSource actual, bool perceptual, CancellationToken cancellationToken)
    {
        var (thumbnailWidth, thumbnailHeight) = GetThumbnailSize(perceptual);
        Span<long> expectedThumbnail = stackalloc long[thumbnailWidth * thumbnailHeight];
        Span<long> actualThumbnail = stackalloc long[thumbnailWidth * thumbnailHeight];

        // Compositing an opaque image over any background leaves it unchanged
        var backgroundCount = expected.IsOpaque(cancellationToken) && actual.IsOpaque(cancellationToken) ? 1 : 2;
        var distance = 0;
        for (var background = 0; background < backgroundCount; background++)
        {
            var whiteBackground = background is 1;
            ComputeThumbnail(expected, whiteBackground, thumbnailWidth, thumbnailHeight, expectedThumbnail, cancellationToken);
            ComputeThumbnail(actual, whiteBackground, thumbnailWidth, thumbnailHeight, actualThumbnail, cancellationToken);

            var hashDistance = perceptual
                ? ComputeHammingDistance(ComputePHash(expectedThumbnail, expected.Width, expected.Height), ComputePHash(actualThumbnail, actual.Width, actual.Height))
                : ComputeHammingDistance(ComputeDHash(expectedThumbnail, expected.Width, expected.Height), ComputeDHash(actualThumbnail, actual.Width, actual.Height));
            var backgroundDistance = hashDistance + ComputeMeanLuminanceDistance(expectedThumbnail, actualThumbnail, expected.Width, expected.Height);
            distance = Math.Max(distance, backgroundDistance);
        }

        return Math.Min(distance, MaxDistance);
    }

    private static (int Width, int Height) GetThumbnailSize(bool perceptual) => perceptual ? (PHashSize, PHashSize) : (DHashWidth, DHashHeight);

    private static int ComputeHammingDistance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);

    /// <summary>
    /// Expresses the difference between the mean luminances on the scale of a 64-bit hash, rounded down: a solid black
    /// image and a solid white image are <see cref="MaxDistance"/> apart, and each unit is a difference of about 4
    /// luminance levels out of 255.
    /// </summary>
    private static int ComputeMeanLuminanceDistance(ReadOnlySpan<long> expectedThumbnail, ReadOnlySpan<long> actualThumbnail, int width, int height)
    {
        // Every source pixel contributes a total weight of thumbnailLength to the thumbnail, and every cell receives a total
        // weight of width × height, so the cells add up to thumbnailLength × Σ pixel values.
        Int128 difference = 0;
        for (var i = 0; i < expectedThumbnail.Length; i++)
        {
            difference += expectedThumbnail[i];
            difference -= actualThumbnail[i];
        }

        var maxDifference = (Int128)expectedThumbnail.Length * width * height * MaxPixelValue;
        return (int)(Int128.Abs(difference) * MaxDistance / maxDifference);
    }

    private static ulong ComputeDHash(ReadOnlySpan<long> thumbnail, int width, int height)
    {
        // All the cells receive the same total weight, width × height, so they compare exactly without being normalized. A
        // bit is set only when the left cell is brighter by more than half a luminance level: with a strict comparison,
        // equal cells, which flat areas produce, flipped on any invisible change.
        var doubledTolerance = (long)width * height * LuminanceLevel;
        ulong hash = 0;
        var bitIndex = 0;
        for (var y = 0; y < DHashHeight; y++)
        {
            var rowOffset = y * DHashWidth;
            for (var x = 0; x < DHashWidth - 1; x++)
            {
                if (2 * (thumbnail[rowOffset + x] - thumbnail[rowOffset + x + 1]) > doubledTolerance)
                {
                    hash |= 1UL << bitIndex;
                }

                bitIndex++;
            }
        }

        return hash;
    }

    private static ulong ComputePHash(ReadOnlySpan<long> thumbnail, int width, int height)
    {
        // Each cell receives a total weight of width × height, so this brings the cells back to luminance levels from 0 to
        // 255, the scale PHashCoefficientTolerance is expressed in.
        var luminanceScale = (double)width * height * LuminanceLevel;
        Span<double> luminance = stackalloc double[PHashSize * PHashSize];
        for (var i = 0; i < luminance.Length; i++)
        {
            luminance[i] = thumbnail[i] / luminanceScale;
        }

        Span<double> horizontalCoefficients = stackalloc double[PHashLowFrequencySize * PHashSize];
        for (var u = 0; u < PHashLowFrequencySize; u++)
        {
            var cosine = CosineTable[u];
            var destinationOffset = u * PHashSize;
            for (var y = 0; y < PHashSize; y++)
            {
                horizontalCoefficients[destinationOffset + y] = DotProduct(luminance.Slice(y * PHashSize, PHashSize), cosine);
            }
        }

        Span<double> coefficients = stackalloc double[PHashLowFrequencySize * PHashLowFrequencySize];
        var coefficientIndex = 0;
        for (var v = 0; v < PHashLowFrequencySize; v++)
        {
            for (var u = 0; u < PHashLowFrequencySize; u++)
            {
                var uScale = u == 0 ? 1 / Math.Sqrt(2) : 1;
                var vScale = v == 0 ? 1 / Math.Sqrt(2) : 1;
                coefficients[coefficientIndex] = DotProduct(horizontalCoefficients.Slice(u * PHashSize, PHashSize), CosineTable[v]) * uScale * vScale;
                coefficientIndex++;
            }
        }

        Span<double> valuesWithoutDc = stackalloc double[coefficients.Length - 1];
        coefficients[1..].CopyTo(valuesWithoutDc);
        valuesWithoutDc.Sort();
        var median = valuesWithoutDc[valuesWithoutDc.Length / 2];

        ulong hash = 0;
        for (var i = 0; i < coefficients.Length; i++)
        {
            if (coefficients[i] > median + PHashCoefficientTolerance)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
    }

    /// <summary>
    /// Computes the area-weighted luminance thumbnail of a frame. Along each axis, source pixel <c>i</c> covers
    /// <c>[i × thumbnailSize, (i + 1) × thumbnailSize)</c> and cell <c>j</c> covers <c>[j × imageSize, (j + 1) × imageSize)</c>,
    /// so all the overlaps are integers. Each cell receives a total weight of <c>width × height</c>.
    /// </summary>
    private static void ComputeThumbnail(PixelSource source, bool whiteBackground, int thumbnailWidth, int thumbnailHeight, Span<long> destination, CancellationToken cancellationToken)
    {
        destination.Clear();

        // The luminance of each source row, reduced horizontally to the width of the thumbnail. Each value is at most
        // width × MaxPixelValue, and each cell at most width × height × MaxPixelValue, so a long does not overflow for any
        // frame the library can hold.
        Span<long> row = stackalloc long[thumbnailWidth];
        var width = source.Width;
        var height = source.Height;
        var firstRowCell = 0;
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            row.Clear();
            var sourceRow = source.GetRow(y);
            var firstColumnCell = 0;
            for (var x = 0; x < sourceRow.Length; x++)
            {
                var value = GetPixelValue(sourceRow[x], whiteBackground);
                var start = (long)x * thumbnailWidth;
                var end = start + thumbnailWidth;
                while ((firstColumnCell + 1L) * width <= start)
                {
                    firstColumnCell++;
                }

                for (var cell = firstColumnCell; (long)cell * width < end; cell++)
                {
                    row[cell] += value * GetOverlap(start, end, cell, width);
                }
            }

            var rowStart = (long)y * thumbnailHeight;
            var rowEnd = rowStart + thumbnailHeight;
            while ((firstRowCell + 1L) * height <= rowStart)
            {
                firstRowCell++;
            }

            for (var cell = firstRowCell; (long)cell * height < rowEnd; cell++)
            {
                var weight = GetOverlap(rowStart, rowEnd, cell, height);
                var destinationRow = destination.Slice(cell * thumbnailWidth, thumbnailWidth);
                for (var x = 0; x < thumbnailWidth; x++)
                {
                    destinationRow[x] += row[x] * weight;
                }
            }
        }
    }

    /// <summary>Gets the length of the overlap of the source interval <c>[start, end)</c> with thumbnail cell <paramref name="cell"/>.</summary>
    private static long GetOverlap(long start, long end, int cell, int sourceSize)
    {
        var cellStart = (long)cell * sourceSize;
        var cellEnd = cellStart + sourceSize;
        return Math.Min(end, cellEnd) - Math.Max(start, cellStart);
    }

    /// <summary>
    /// Computes the luminance of a pixel composited over a black or white background, premultiplied by 255 so that it is
    /// an integer from <c>0</c> to <see cref="MaxPixelValue"/>.
    /// </summary>
    private static long GetPixelValue(Rgba32 pixel, bool whiteBackground)
    {
        var luminance = RedWeight * pixel.R + GreenWeight * pixel.G + BlueWeight * pixel.B;
        var value = luminance * pixel.A;
        if (whiteBackground)
        {
            value += (RedWeight + GreenWeight + BlueWeight) * 255 * (255 - pixel.A);
        }

        return value;
    }

    private static double DotProduct(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        // A scalar loop in a fixed order, so the result does not depend on the vector width of the hardware
        var sum = 0d;
        for (var i = 0; i < left.Length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }

    private static double[][] CreateCosineTable()
    {
        var result = new double[PHashLowFrequencySize][];
        for (var frequency = 0; frequency < PHashLowFrequencySize; frequency++)
        {
            result[frequency] = new double[PHashSize];
            for (var position = 0; position < PHashSize; position++)
            {
                result[frequency][position] = Math.Cos((2 * position + 1) * frequency * Math.PI / (2 * PHashSize));
            }
        }

        return result;
    }
}
