using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// Builds a realistic progressive JPEG (SOF2) from source pixels: full-range YCbCr 4:2:0 (2x2 box-filtered chroma), a
/// direct double-precision forward DCT, quantization with the Annex K tables scaled for a quality, then the independent
/// test-side progressive writer of the unit tests (<see cref="JpegTestImage"/>, linked source) with the usual
/// successive-approximation progression. The library encoder only writes baseline JPEG, so this is the only way to get
/// large progressive inputs without committing them or depending on an external tool.
/// </summary>
internal static class ProgressiveJpegInput
{
    /// <summary>
    /// The usual ten-scan successive-approximation progression of YCbCr images: DC first (Al = 1), luma low AC bands,
    /// chroma AC, luma high AC band, AC refinements, DC refinement, final AC refinements.
    /// </summary>
    public const string StandardScript = "0,1,2: 0 0 0 1; 0: 1 5 0 2; 2: 1 63 0 1; 1: 1 63 0 1; 0: 6 63 0 2; 0: 1 63 2 1; 0,1,2: 0 0 1 0; 2: 1 63 1 0; 1: 1 63 1 0; 0: 1 63 1 0";

    /// <summary>Encodes 8-bit RGB samples as a progressive 4:2:0 JPEG.</summary>
    public static byte[] Encode(byte[] rgb, int width, int height, int quality)
        => CreateCoefficients(rgb, width, height, quality).Encode(new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript(StandardScript) });

    /// <summary>Gets the quantized coefficients of 8-bit RGB samples (4:2:0); encode them sequentially or progressively.</summary>
    public static JpegTestImage CreateCoefficients(byte[] rgb, int width, int height, int quality)
    {
        var tables = new ushort[2][];
        tables[0] = new ushort[64];
        tables[1] = new ushort[64];
        JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.LuminanceQuantization, quality, tables[0]);
        JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.ChrominanceQuantization, quality, tables[1]);

        // Planes padded to whole 16x16 MCUs by edge replication
        var paddedWidth = (width + 15) / 16 * 16;
        var paddedHeight = (height + 15) / 16 * 16;
        var luma = new double[paddedWidth * paddedHeight];
        var cb = new double[paddedWidth * paddedHeight];
        var cr = new double[paddedWidth * paddedHeight];
        for (var y = 0; y < paddedHeight; y++)
        {
            for (var x = 0; x < paddedWidth; x++)
            {
                var offset = ((Math.Min(y, height - 1) * width) + Math.Min(x, width - 1)) * 3;
                double r = rgb[offset];
                double g = rgb[offset + 1];
                double b = rgb[offset + 2];
                var index = (y * paddedWidth) + x;
                luma[index] = (0.299 * r) + (0.587 * g) + (0.114 * b);
                cb[index] = (-0.168736 * r) - (0.331264 * g) + (0.5 * b) + 128;
                cr[index] = (0.5 * r) - (0.418688 * g) - (0.081312 * b) + 128;
            }
        }

        var chromaWidth = paddedWidth / 2;
        var chromaHeight = paddedHeight / 2;
        var planes = new[] { (luma, paddedWidth), (Downsample(cb, paddedWidth, chromaWidth, chromaHeight), chromaWidth), (Downsample(cr, paddedWidth, chromaWidth, chromaHeight), chromaWidth) };
        var cosines = new double[8][];
        for (var x = 0; x < 8; x++)
        {
            cosines[x] = new double[8];
            for (var u = 0; u < 8; u++)
            {
                cosines[x][u] = Math.Cos(((2 * x) + 1) * u * Math.PI / 16);
            }
        }

        return JpegTestImage.FromCoefficients(width, height, [(2, 2), (1, 1), (1, 1)], tables, (component, bx, by) =>
        {
            var (plane, stride) = planes[component];
            var quantization = tables[component == 0 ? 0 : 1];
            var block = new int[64];
            Span<double> rows = stackalloc double[64];
            for (var y = 0; y < 8; y++)
            {
                for (var u = 0; u < 8; u++)
                {
                    var sum = 0.0;
                    for (var x = 0; x < 8; x++)
                    {
                        sum += (plane[(((by * 8) + y) * stride) + (bx * 8) + x] - 128) * cosines[x][u];
                    }

                    rows[(y * 8) + u] = sum;
                }
            }

            for (var v = 0; v < 8; v++)
            {
                for (var u = 0; u < 8; u++)
                {
                    var sum = 0.0;
                    for (var y = 0; y < 8; y++)
                    {
                        sum += rows[(y * 8) + u] * cosines[y][v];
                    }

                    var scale = (u == 0 ? 1 / Math.Sqrt(2) : 1) * (v == 0 ? 1 / Math.Sqrt(2) : 1) / 4;
                    block[(v * 8) + u] = (int)Math.Round(sum * scale / quantization[(v * 8) + u], MidpointRounding.AwayFromZero);
                }
            }

            return block;
        });
    }

    private static double[] Downsample(double[] plane, int stride, int width, int height)
    {
        var result = new double[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (2 * y * stride) + (2 * x);
                result[(y * width) + x] = (plane[index] + plane[index + 1] + plane[index + stride] + plane[index + stride + 1]) / 4;
            }
        }

        return result;
    }
}
