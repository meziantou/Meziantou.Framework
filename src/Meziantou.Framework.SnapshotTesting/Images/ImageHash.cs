using System.Numerics;

namespace Meziantou.Framework.SnapshotTesting;

internal static class ImageHash
{
    private const int DHashWidth = 9;
    private const int DHashHeight = 8;
    private const int PHashSize = 32;
    private const int PHashLowFrequencySize = 8;
    private static readonly double[][] CosineTable = CreateCosineTable();

    public static ulong ComputeDHash(Image image)
    {
        Span<double> luminance = stackalloc double[DHashWidth * DHashHeight];
        ResizeToLuminance(image, DHashWidth, DHashHeight, luminance);

        ulong hash = 0;
        var bitIndex = 0;
        for (var y = 0; y < DHashHeight; y++)
        {
            var rowOffset = y * DHashWidth;
            for (var x = 0; x < DHashWidth - 1; x++)
            {
                if (luminance[rowOffset + x] > luminance[rowOffset + x + 1])
                    hash |= 1UL << bitIndex;

                bitIndex++;
            }
        }

        return hash;
    }

    public static ulong ComputePHash(Image image)
    {
        Span<double> luminance = stackalloc double[PHashSize * PHashSize];
        ResizeToLuminance(image, PHashSize, PHashSize, luminance);

        Span<double> coefficients = stackalloc double[PHashLowFrequencySize * PHashLowFrequencySize];
        var coefficientIndex = 0;
        for (var v = 0; v < PHashLowFrequencySize; v++)
        {
            for (var u = 0; u < PHashLowFrequencySize; u++)
            {
                double coefficient = 0;
                for (var y = 0; y < PHashSize; y++)
                {
                    var rowOffset = y * PHashSize;
                    for (var x = 0; x < PHashSize; x++)
                    {
                        coefficient += luminance[rowOffset + x] * CosineTable[x][u] * CosineTable[y][v];
                    }
                }

                var uScale = u == 0 ? 1 / Math.Sqrt(2) : 1;
                var vScale = v == 0 ? 1 / Math.Sqrt(2) : 1;
                coefficients[coefficientIndex] = coefficient * uScale * vScale;
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
            if (coefficients[i] > median)
                hash |= 1UL << i;
        }

        return hash;
    }

    public static int ComputeHammingDistance(ulong left, ulong right)
    {
        return BitOperations.PopCount(left ^ right);
    }

    private static void ResizeToLuminance(Image image, int width, int height, Span<double> destination)
    {
        var pixels = image.Pixels.Span;
        for (var y = 0; y < height; y++)
        {
            var sourceY = (y + 0.5) * image.Height / height - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, image.Height - 1);
            var y1 = Math.Min(y0 + 1, image.Height - 1);
            var yWeight = Math.Clamp(sourceY - y0, 0, 1);

            for (var x = 0; x < width; x++)
            {
                var sourceX = (x + 0.5) * image.Width / width - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, image.Width - 1);
                var x1 = Math.Min(x0 + 1, image.Width - 1);
                var xWeight = Math.Clamp(sourceX - x0, 0, 1);

                var top = Lerp(GetLuminance(pixels[y0 * image.Width + x0]), GetLuminance(pixels[y0 * image.Width + x1]), xWeight);
                var bottom = Lerp(GetLuminance(pixels[y1 * image.Width + x0]), GetLuminance(pixels[y1 * image.Width + x1]), xWeight);
                destination[y * width + x] = Lerp(top, bottom, yWeight);
            }
        }
    }

    private static double GetLuminance(Argb pixel)
    {
        return 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
    }

    private static double Lerp(double left, double right, double amount)
    {
        return left + (right - left) * amount;
    }

    private static double[][] CreateCosineTable()
    {
        var result = new double[PHashSize][];
        for (var position = 0; position < PHashSize; position++)
        {
            result[position] = new double[PHashLowFrequencySize];
            for (var frequency = 0; frequency < PHashLowFrequencySize; frequency++)
            {
                result[position][frequency] = Math.Cos((2 * position + 1) * frequency * Math.PI / (2 * PHashSize));
            }
        }

        return result;
    }
}
