using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Computes the 64-bit difference hash (dHash) and perceptual hash (pHash) of images, and the distance between
/// two images according to each hash.
/// </summary>
/// <remarks>
/// <para>
/// Both hashes start from a small luminance thumbnail (9×8 for dHash, 32×32 for pHash). Every source pixel
/// contributes to the thumbnail cells it overlaps in proportion to the overlapping area, so a change anywhere in
/// the image affects the thumbnail. The luminance uses the BT.601 weights and the thumbnail is computed with integer
/// arithmetic, so it is exact: uniform areas produce exactly equal cells, and the hashes do not depend on the
/// hardware.
/// </para>
/// <para>
/// Hashes only describe the structure of an image, not its overall brightness, and transparent pixels have no
/// luminance of their own. <see cref="ComputeDHashDistance"/> and <see cref="ComputePHashDistance"/> therefore add
/// the difference between the mean luminances to the Hamming distance, and evaluate images that are not fully opaque
/// composited over both a black and a white background, keeping the larger distance.
/// </para>
/// </remarks>
internal static class ImageHash
{
    public const int MaxDistance = 64;

    private const int DHashWidth = 9;
    private const int DHashHeight = 8;
    private const int PHashSize = 32;
    private const int PHashLowFrequencySize = 8;

    // BT.601 luminance weights, scaled so that they are integers
    private const long RedWeight = 299;
    private const long GreenWeight = 587;
    private const long BlueWeight = 114;

    // The luminance of a white pixel, premultiplied by an opaque alpha
    private const long MaxPixelValue = (RedWeight + GreenWeight + BlueWeight) * 255 * 255;

    /// <summary>
    /// Coefficients closer to the median than this are considered equal to it. The DCT of an exact thumbnail
    /// only carries rounding noise at this scale, which would otherwise decide the bits of flat images.
    /// </summary>
    private const double PHashCoefficientTolerance = 1e-6;

    private static readonly double[][] CosineTable = CreateCosineTable();

    /// <summary>
    /// Computes the dHash of an image composited over a black background.
    /// </summary>
    public static ulong ComputeDHash(Image image)
    {
        Span<long> thumbnail = stackalloc long[DHashWidth * DHashHeight];
        ComputeThumbnail(image, whiteBackground: false, DHashWidth, DHashHeight, thumbnail);
        return ComputeDHash(thumbnail);
    }

    /// <summary>
    /// Computes the pHash of an image composited over a black background.
    /// </summary>
    public static ulong ComputePHash(Image image)
    {
        Span<long> thumbnail = stackalloc long[PHashSize * PHashSize];
        ComputeThumbnail(image, whiteBackground: false, PHashSize, PHashSize, thumbnail);
        return ComputePHash(thumbnail, image);
    }

    public static int ComputeHammingDistance(ulong left, ulong right)
    {
        return BitOperations.PopCount(left ^ right);
    }

    /// <summary>
    /// Computes the dHash distance between two images of the same size, from <c>0</c> to <see cref="MaxDistance"/>.
    /// </summary>
    public static int ComputeDHashDistance(Image expected, Image actual) => ComputeDistance(expected, actual, perceptual: false);

    /// <summary>
    /// Computes the pHash distance between two images of the same size, from <c>0</c> to <see cref="MaxDistance"/>.
    /// </summary>
    public static int ComputePHashDistance(Image expected, Image actual) => ComputeDistance(expected, actual, perceptual: true);

    private static int ComputeDistance(Image expected, Image actual, bool perceptual)
    {
        Debug.Assert(expected.Width == actual.Width && expected.Height == actual.Height, "The images must have the same size.");

        var (thumbnailWidth, thumbnailHeight) = perceptual ? (PHashSize, PHashSize) : (DHashWidth, DHashHeight);
        Span<long> expectedThumbnail = stackalloc long[thumbnailWidth * thumbnailHeight];
        Span<long> actualThumbnail = stackalloc long[thumbnailWidth * thumbnailHeight];

        // Compositing an opaque image over any background leaves it unchanged
        var backgroundCount = IsOpaque(expected) && IsOpaque(actual) ? 1 : 2;
        var distance = 0;
        for (var background = 0; background < backgroundCount; background++)
        {
            var whiteBackground = background is 1;
            ComputeThumbnail(expected, whiteBackground, thumbnailWidth, thumbnailHeight, expectedThumbnail);
            ComputeThumbnail(actual, whiteBackground, thumbnailWidth, thumbnailHeight, actualThumbnail);

            var hashDistance = perceptual
                ? ComputeHammingDistance(ComputePHash(expectedThumbnail, expected), ComputePHash(actualThumbnail, actual))
                : ComputeHammingDistance(ComputeDHash(expectedThumbnail), ComputeDHash(actualThumbnail));
            var backgroundDistance = hashDistance
                + ComputeMeanLuminanceDistance(expectedThumbnail, actualThumbnail, expected.Width, expected.Height);
            distance = Math.Max(distance, backgroundDistance);
        }

        return Math.Min(distance, MaxDistance);
    }

    /// <summary>
    /// Expresses the difference between the mean luminances on the scale of a 64-bit hash, rounded down: a
    /// solid black image and a solid white image are <see cref="MaxDistance"/> apart, and each unit is a
    /// difference of about 4 luminance levels out of 255.
    /// </summary>
    private static int ComputeMeanLuminanceDistance(ReadOnlySpan<long> expectedThumbnail, ReadOnlySpan<long> actualThumbnail, int width, int height)
    {
        // Every source pixel contributes a total weight of thumbnailLength to the thumbnail, and every cell
        // receives a total weight of width × height, so the cells add up to thumbnailLength × Σ pixel values.
        Int128 difference = 0;
        for (var i = 0; i < expectedThumbnail.Length; i++)
        {
            difference += expectedThumbnail[i];
            difference -= actualThumbnail[i];
        }

        var maxDifference = (Int128)expectedThumbnail.Length * width * height * MaxPixelValue;
        return (int)(Int128.Abs(difference) * MaxDistance / maxDifference);
    }

    private static ulong ComputeDHash(ReadOnlySpan<long> thumbnail)
    {
        // All the cells receive the same total weight, so they compare exactly without being normalized
        ulong hash = 0;
        var bitIndex = 0;
        for (var y = 0; y < DHashHeight; y++)
        {
            var rowOffset = y * DHashWidth;
            for (var x = 0; x < DHashWidth - 1; x++)
            {
                if (thumbnail[rowOffset + x] > thumbnail[rowOffset + x + 1])
                    hash |= 1UL << bitIndex;

                bitIndex++;
            }
        }

        return hash;
    }

    private static ulong ComputePHash(ReadOnlySpan<long> thumbnail, Image image)
    {
        // Each cell receives a total weight of width × height, so this brings the cells back to luminance levels
        // from 0 to 255, the scale PHashCoefficientTolerance is expressed in.
        var luminanceScale = (double)image.Width * image.Height * (MaxPixelValue / 255);
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
                hash |= 1UL << i;
        }

        return hash;
    }

    private static bool IsOpaque(Image image)
    {
        foreach (var pixel in image.Pixels.Span)
        {
            if (pixel.A is not 255)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Computes the area-weighted luminance thumbnail of an image. Along each axis, source pixel <c>i</c> covers
    /// <c>[i × thumbnailSize, (i + 1) × thumbnailSize)</c> and cell <c>j</c> covers
    /// <c>[j × imageSize, (j + 1) × imageSize)</c>, so all the overlaps are integers. Each cell receives a total
    /// weight of <c>image.Width × image.Height</c>.
    /// </summary>
    private static void ComputeThumbnail(Image image, bool whiteBackground, int thumbnailWidth, int thumbnailHeight, Span<long> destination)
    {
        destination.Clear();

        var columns = AxisContributions.Create(image.Width, thumbnailWidth);
        var rows = AxisContributions.Create(image.Height, thumbnailHeight);

        // The luminance of each source row, reduced horizontally to the width of the thumbnail. Each value is at
        // most image.Width × MaxPixelValue, and each cell at most image.Width × image.Height × MaxPixelValue, so
        // a long does not overflow for any image an array can hold.
        Span<long> row = stackalloc long[thumbnailWidth];
        var pixels = image.Pixels.Span;
        for (var y = 0; y < image.Height; y++)
        {
            row.Clear();
            var sourceRow = pixels.Slice(y * image.Width, image.Width);
            for (var x = 0; x < sourceRow.Length; x++)
            {
                var value = GetPixelValue(sourceRow[x], whiteBackground);
                for (var i = columns.Offsets[x]; i < columns.Offsets[x + 1]; i++)
                {
                    row[columns.Cells[i]] += value * columns.Weights[i];
                }
            }

            for (var i = rows.Offsets[y]; i < rows.Offsets[y + 1]; i++)
            {
                var weight = rows.Weights[i];
                var destinationRow = destination.Slice(rows.Cells[i] * thumbnailWidth, thumbnailWidth);
                for (var x = 0; x < thumbnailWidth; x++)
                {
                    destinationRow[x] += row[x] * weight;
                }
            }
        }
    }

    /// <summary>
    /// Computes the luminance of a pixel composited over a black or white background, premultiplied by 255 so that
    /// it is an integer from <c>0</c> to <see cref="MaxPixelValue"/>.
    /// </summary>
    private static long GetPixelValue(Argb pixel, bool whiteBackground)
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

    /// <summary>
    /// The cells of a thumbnail axis each source pixel overlaps, and by how much: the contributions of source pixel
    /// <c>i</c> are the entries from <c>Offsets[i]</c> to <c>Offsets[i + 1]</c>.
    /// </summary>
    private readonly record struct AxisContributions(int[] Offsets, int[] Cells, long[] Weights)
    {
        public static AxisContributions Create(int sourceSize, int thumbnailSize)
        {
            var offsets = new int[sourceSize + 1];
            for (var i = 0; i < sourceSize; i++)
            {
                var (firstCell, lastCell) = GetCellRange(i, sourceSize, thumbnailSize);
                offsets[i + 1] = offsets[i] + lastCell - firstCell + 1;
            }

            var cells = new int[offsets[sourceSize]];
            var weights = new long[offsets[sourceSize]];
            for (var i = 0; i < sourceSize; i++)
            {
                var start = (long)i * thumbnailSize;
                var end = start + thumbnailSize;
                var (firstCell, lastCell) = GetCellRange(i, sourceSize, thumbnailSize);
                for (var cell = firstCell; cell <= lastCell; cell++)
                {
                    var cellStart = (long)cell * sourceSize;
                    var cellEnd = cellStart + sourceSize;
                    var index = offsets[i] + cell - firstCell;
                    cells[index] = cell;
                    weights[index] = Math.Min(end, cellEnd) - Math.Max(start, cellStart);
                }
            }

            return new AxisContributions(offsets, cells, weights);
        }

        private static (int FirstCell, int LastCell) GetCellRange(int sourceIndex, int sourceSize, int thumbnailSize)
        {
            var start = (long)sourceIndex * thumbnailSize;
            var end = start + thumbnailSize;
            return ((int)(start / sourceSize), (int)((end - 1) / sourceSize));
        }
    }
}
