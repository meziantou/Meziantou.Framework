using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    // Coefficients that make the sums leave the range of the samples, overflow to infinities, or cancel out
    private static readonly double[] SpecialCoefficients = [0, 0, 0, 1, 1, -1, 0.5, 2, -0.25, 1e300, -1e300, 1e308, -1e308, 1e-300, 0.2126, 0.7152];

    /// <summary>
    /// <see cref="ImageProcessingExtensions.ApplyColorMatrix(Image, ColorMatrix, CancellationToken)"/> on the formats with
    /// vector kernels (several pixels per vector, and a variant for the matrices that keep the alpha): a byte of pixel
    /// format, a byte of width (1 to 64) and one of height (1 to 3), twenty bytes of coefficients (a special value below
    /// 16, a multiple of 1/32 between -4 and 4 otherwise), then the samples, repeated when the input is shorter. Every
    /// sample must be the one of the documented formula, computed here in double precision with the same order of operations.
    /// The bit 2 of the byte of pixel format makes the rows of the matrix read one sample each (the other coefficients of
    /// the samples are zero), as brightness, contrast, invert and opacity do: Arm64 looks them up in tables.
    /// </summary>
    private static void ColorMatrixTarget(ReadOnlySpan<byte> data)
    {
        var formatByte = FuzzInput.ReadByte(ref data);
        var format = formatByte % 4;
        var isDiagonal = (formatByte & 4) != 0;
        var width = 1 + (FuzzInput.ReadByte(ref data) % 64);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 3);
        var values = new double[20];
        for (var i = 0; i < values.Length; i++)
        {
            var b = FuzzInput.ReadByte(ref data);
            values[i] = b < SpecialCoefficients.Length ? SpecialCoefficients[b] : (b - 136) / 32.0;
        }

        // Half of the inputs keep the alpha, as the filter functions of CSS do
        if ((values[19] is 0 or 1) && !data.IsEmpty && (data[0] & 1) == 0)
        {
            values[3] = values[8] = values[13] = 0;
            values[15] = values[16] = values[17] = values[19] = 0;
            values[18] = 1;
        }

        if (isDiagonal)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    if (column != row)
                    {
                        values[(row * 5) + column] = 0;
                    }
                }
            }
        }

        var matrix = new ColorMatrix(values);
        var (bytesPerPixel, channels, max) = format switch
        {
            0 or 1 => (4, 4, 255),
            2 => (3, 3, 255),
            _ => (8, 4, 65535),
        };
        var bytes = new byte[width * height * bytesPerPixel];
        for (var i = 0; i < bytes.Length && !data.IsEmpty; i++)
        {
            bytes[i] = data[i % data.Length];
        }

        var samples = new int[width * height * channels];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = max == 255 ? bytes[i] : bytes[2 * i] | (bytes[(2 * i) + 1] << 8);
        }

        using var image = format switch
        {
            0 => (Image)Image.ImportPixelBytes<Rgba32>(bytes, width, height),
            1 => Image.ImportPixelBytes<Bgra32>(bytes, width, height),
            2 => Image.ImportPixelBytes<Rgb24>(bytes, width, height),
            _ => Image.ImportPixelBytes<Rgba64>(bytes, width, height),
        };
        image.ApplyColorMatrix(matrix);
        var result = new byte[bytes.Length];
        image.Frames[0].CopyPixelBytesTo(result);

        for (var p = 0; p < width * height; p++)
        {
            var pixel = samples.AsSpan(p * channels, channels);
            var (r, g, b) = format == 1 ? (pixel[2], pixel[1], pixel[0]) : (pixel[0], pixel[1], pixel[2]);
            var a = channels == 4 ? pixel[3] : max;
            var red = Row(0);
            var green = Row(1);
            var blue = Row(2);
            var alpha = Row(3);
            int[] expected = format switch
            {
                0 or 3 => [red, green, blue, alpha],
                1 => [blue, green, red, alpha],
                _ => [OverBlack(red), OverBlack(green), OverBlack(blue)],
            };
            for (var c = 0; c < channels; c++)
            {
                var actual = max == 255 ? result[(p * channels) + c] : result[2 * ((p * channels) + c)] | (result[(2 * ((p * channels) + c)) + 1] << 8);
                FuzzAssert.Equal(expected[c], actual, $"Sample {c} of pixel {p} (format {format})");
            }

            int Row(int row)
            {
                var m = values.AsSpan(row * 5, 5);
                var value = Math.Floor((m[0] * r) + (m[1] * g) + (m[2] * b) + (m[3] * a) + (m[4] * max) + (0.5 + (1.0 / 1048576)));
                return value >= max ? max : value > 0 ? (int)value : 0;
            }

            int OverBlack(int color) => ((color * alpha) + (max / 2)) / max;
        }
    }

    private static IEnumerable<FuzzSeed> GetColorMatrixSeeds()
    {
        // The matrices of the unit tests: sepia (keeps the alpha), opacity, sums beyond the samples and infinities, on
        // widths around the groups of 4 and 8 pixels and their tails
        yield return new FuzzSeed("rgba32-sepia-like", (byte[])[0, 18, 1, 161, 197, 142, 16, 136, 148, 186, 137, 16, 136, 141, 158, 132, 16, 136, 0, 0, 0, 3, 0, 0, 10, 200, 30, 255, 99, 0, 7, 128]);
        yield return new FuzzSeed("bgra32-opacity", (byte[])[1, 7, 0, 3, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 148, 0, 1, 250, 128, 0, 64]);
        yield return new FuzzSeed("rgb24-infinities", (byte[])[2, 33, 2, 10, 12, 6, 3, 0, 11, 13, 9, 0, 3, 200, 100, 3, 0, 0, 0, 0, 0, 3, 0, 1, 77, 0, 255]);
        yield return new FuzzSeed("rgba64-random", (byte[])[3, 9, 1, 40, 200, 100, 150, 20, 220, 60, 33, 140, 70, 90, 180, 250, 17, 131, 160, 3, 0, 255, 255, 0, 0, 18, 52, 86, 120]);
        yield return new FuzzSeed("rgba32-diagonal", (byte[])[4, 47, 2, 180, 0, 0, 0, 20, 0, 150, 0, 0, 120, 0, 0, 100, 0, 9, 0, 0, 0, 120, 0, 1, 250, 128, 0, 64, 17, 200]);
        yield return new FuzzSeed("bgra32-diagonal-alpha-kept", (byte[])[5, 35, 1, 140, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 90, 0, 7, 0, 0, 0, 3, 0, 0, 10, 220, 30, 255, 99, 0]);
        yield return new FuzzSeed("rgb24-diagonal", (byte[])[6, 63, 0, 160, 0, 0, 0, 12, 0, 120, 0, 0, 10, 0, 0, 170, 0, 4, 0, 0, 0, 3, 0, 1, 77, 0, 255, 128, 64]);
        yield return new FuzzSeed("rgba32-single-pixel", (byte[])[0, 0, 0, 5, 0, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 3, 0, 1, 200, 100, 50, 25]);
    }
}
