using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>
/// An independent transcription of the JPEG encoder contract used to check encoder output at the
/// coefficient level, without any decoder: from 8-bit RGB or gray source pixels it computes the exact (unrounded) quantized
/// DCT coefficients the encoder must produce. Steps: edge replication to whole MCUs; full-range YCbCr (ITU-R BT.601 matrix
/// as printed in JFIF 1.02); chroma box filter (the mean of the H x V luma-resolution samples a chroma sample covers);
/// level shift; the direct (non-separable) 2-D forward DCT of T.81 A.3.3 with <see cref="Math.Cos"/>; division by the
/// quantization value. It also transcribes the Annex K tables and the quality scaling formula.
/// </summary>
public static class JpegEncoderModel
{
    private static readonly double[] Cosines = CreateCosines();

    /// <summary>Gets the luminance table of T.81 Annex K (table K.1), natural order.</summary>
    public static IReadOnlyList<int> AnnexKLuminance { get; } =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    /// <summary>Gets the chrominance table of T.81 Annex K (table K.2), natural order.</summary>
    public static IReadOnlyList<int> AnnexKChrominance { get; } = CreateChrominance();

    /// <summary>Scales an Annex K table: <c>s = q &lt; 50 ? 5000 / q : 200 - 2q</c>, <c>clamp((base * s + 50) / 100, 1, 255)</c>.</summary>
    /// <param name="basis">The table.</param>
    /// <param name="quality">The quality, 1 to 100.</param>
    /// <returns>The scaled table.</returns>
    public static int[] ScaleTable(IReadOnlyList<int> basis, int quality)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var scale = quality < 50 ? 5000 / quality : 200 - (2 * quality);
        return [.. basis.Select(value => Math.Clamp(((value * scale) + 50) / 100, 1, 255))];
    }

    /// <summary>Computes the expected quantized coefficients (unrounded) of every block of every component.</summary>
    /// <param name="source">The source pixels (<see cref="RawPixelLayout.Rgb8"/> for YCbCr output, <see cref="RawPixelLayout.Gray8"/> for grayscale output).</param>
    /// <param name="h">The luma horizontal sampling factor (1 for grayscale).</param>
    /// <param name="v">The luma vertical sampling factor (1 for grayscale).</param>
    /// <param name="luminance">The luminance quantization table (natural order).</param>
    /// <param name="chrominance">The chrominance quantization table (natural order), ignored for grayscale.</param>
    /// <returns>Per component, 64 values per block (natural order) in the block order of <see cref="ReferenceJpeg.DecodeCoefficients"/>.</returns>
    public static IReadOnlyList<ReferenceJpegModelBlocks> ComputeCoefficients(RawPixelBuffer source, int h, int v, IReadOnlyList<int> luminance, IReadOnlyList<int> chrominance)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(luminance);
        ArgumentNullException.ThrowIfNull(chrominance);
        var gray = source.Layout == RawPixelLayout.Gray8;
        if (!gray && source.Layout != RawPixelLayout.Rgb8)
            throw new ArgumentException("The model takes 8-bit RGB or gray pixels.", nameof(source));

        var mcusX = (source.Width + (8 * h) - 1) / (8 * h);
        var mcusY = (source.Height + (8 * v) - 1) / (8 * v);
        var paddedWidth = mcusX * 8 * h;
        var paddedHeight = mcusY * 8 * v;
        var planes = new double[gray ? 1 : 3][];
        for (var c = 0; c < planes.Length; c++)
        {
            planes[c] = new double[paddedWidth * paddedHeight];
        }

        for (var y = 0; y < paddedHeight; y++)
        {
            for (var x = 0; x < paddedWidth; x++)
            {
                var sx = Math.Min(x, source.Width - 1);
                var sy = Math.Min(y, source.Height - 1);
                var index = (y * paddedWidth) + x;
                if (gray)
                {
                    planes[0][index] = source.GetSample(sx, sy, 0) - 128d;
                    continue;
                }

                double r = source.GetSample(sx, sy, 0);
                double g = source.GetSample(sx, sy, 1);
                double b = source.GetSample(sx, sy, 2);
                planes[0][index] = (0.299 * r) + (0.587 * g) + (0.114 * b) - 128;
                planes[1][index] = (-0.168735892 * r) - (0.331264108 * g) + (0.5 * b);
                planes[2][index] = (0.5 * r) - (0.418687589 * g) - (0.081312411 * b);
            }
        }

        var result = new List<ReferenceJpegModelBlocks>();
        for (var c = 0; c < planes.Length; c++)
        {
            var factorX = c == 0 ? 1 : h;
            var factorY = c == 0 ? 1 : v;
            var blocksX = paddedWidth / (8 * factorX);
            var blocksY = paddedHeight / (8 * factorY);
            var table = c == 0 ? luminance : chrominance;
            var values = new double[blocksX * blocksY * 64];
            var samples = new double[64];
            for (var by = 0; by < blocksY; by++)
            {
                for (var bx = 0; bx < blocksX; bx++)
                {
                    for (var y = 0; y < 8; y++)
                    {
                        for (var x = 0; x < 8; x++)
                        {
                            var sum = 0d;
                            for (var dy = 0; dy < factorY; dy++)
                            {
                                for (var dx = 0; dx < factorX; dx++)
                                {
                                    var px = (((bx * 8) + x) * factorX) + dx;
                                    var py = (((by * 8) + y) * factorY) + dy;
                                    sum += planes[c][(py * paddedWidth) + px];
                                }
                            }

                            samples[(y * 8) + x] = sum / (factorX * factorY);
                        }
                    }

                    var offset = ((by * blocksX) + bx) * 64;
                    for (var v2 = 0; v2 < 8; v2++)
                    {
                        for (var u = 0; u < 8; u++)
                        {
                            // F(u, v) = 1/4 C(u) C(v) sum_x sum_y s(x, y) cos((2x+1) u pi/16) cos((2y+1) v pi/16)
                            var sum = 0d;
                            for (var y = 0; y < 8; y++)
                            {
                                for (var x = 0; x < 8; x++)
                                {
                                    sum += samples[(y * 8) + x] * Cosines[(u * 8) + x] * Cosines[(v2 * 8) + y];
                                }
                            }

                            values[offset + (v2 * 8) + u] = sum / 4 / table[(v2 * 8) + u];
                        }
                    }
                }
            }

            result.Add(new ReferenceJpegModelBlocks(blocksX, blocksY, values));
        }

        return result;
    }

    /// <summary>
    /// Compares decoded coefficients with the model: each must equal the model value rounded to nearest (ties away from
    /// zero) and clamped to the baseline range, except within <paramref name="tieWindow"/> of a rounding boundary, where
    /// either neighbor is accepted (single-precision sample storage moves coefficients by far less than 1e-3).
    /// </summary>
    /// <param name="expected">The model coefficients.</param>
    /// <param name="actual">The decoded coefficients.</param>
    /// <param name="tieWindow">The half-width of the accepted window around a rounding boundary.</param>
    /// <returns>The comparison.</returns>
    public static JpegCoefficientComparison Compare(IReadOnlyList<ReferenceJpegModelBlocks> expected, IReadOnlyList<ReferenceJpegBlocks> actual, double tieWindow = 1e-3)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Count != actual.Count)
            return new JpegCoefficientComparison(0, 0, 1, string.Create(CultureInfo.InvariantCulture, $"{actual.Count} component(s) instead of {expected.Count}."));

        long compared = 0;
        long nearTies = 0;
        long mismatches = 0;
        string? first = null;
        for (var c = 0; c < expected.Count; c++)
        {
            var model = expected[c];
            var grid = actual[c];
            if (model.BlocksX != grid.BlocksX || model.BlocksY != grid.BlocksY)
                return new JpegCoefficientComparison(compared, nearTies, mismatches + 1, string.Create(CultureInfo.InvariantCulture, $"Component {c}: {grid.BlocksX}x{grid.BlocksY} blocks instead of {model.BlocksX}x{model.BlocksY}."));

            var values = model.Values.Span;
            var coefficients = grid.Coefficients.Span;
            for (var i = 0; i < values.Length; i++)
            {
                compared++;
                var exact = values[i];
                var dc = i % 64 == 0;
                var rounded = Math.Clamp((int)Math.Round(exact, MidpointRounding.AwayFromZero), dc ? -1024 : -1023, 1023);
                var value = coefficients[i];
                if (value == rounded)
                    continue;

                var fraction = Math.Abs(exact) - Math.Floor(Math.Abs(exact));
                if (Math.Abs(fraction - 0.5) < tieWindow && Math.Abs(value - exact) < 0.5 + tieWindow)
                {
                    nearTies++;
                    continue;
                }

                mismatches++;
                first ??= string.Create(CultureInfo.InvariantCulture, $"Component {c}, block {i / 64 % model.BlocksX},{i / 64 / model.BlocksX}, coefficient (u {i % 8}, v {i % 64 / 8}): {value} instead of {exact:F4} (rounded {rounded}).");
            }
        }

        return new JpegCoefficientComparison(compared, nearTies, mismatches, first);
    }

    private static int[] CreateChrominance()
    {
        var table = Enumerable.Repeat(99, 64).ToArray();
        int[][] rows = [[17, 18, 24, 47], [18, 21, 26, 66], [24, 26, 56], [47, 66]];
        for (var y = 0; y < rows.Length; y++)
        {
            rows[y].CopyTo(table, y * 8);
        }

        return table;
    }

    private static double[] CreateCosines()
    {
        // C(u) cos((2x + 1) u pi / 16), indexed [u * 8 + x]
        var cosines = new double[64];
        for (var u = 0; u < 8; u++)
        {
            for (var x = 0; x < 8; x++)
            {
                cosines[(u * 8) + x] = (u == 0 ? 1 / Math.Sqrt(2) : 1) * Math.Cos(((2 * x) + 1) * u * Math.PI / 16);
            }
        }

        return cosines;
    }
}
