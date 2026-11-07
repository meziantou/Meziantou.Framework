namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the pixels of a frame as linear sRGB planes in [0, 1], composited over a gray background
/// (<see cref="Ssimulacra2Calculator"/>, <see cref="ButteraugliCalculator"/>).
/// </summary>
/// <remarks>
/// <para>
/// The composition is done on the encoded values, as the libjxl tools do: <c>a × c + (1 − a) × background</c>, with
/// <c>c</c> and <c>a</c> the 8-bit samples multiplied by <c>1/255</c>. A fully opaque pixel keeps its color whatever the background.
/// </para>
/// <para>
/// The sRGB transfer function is removed with the rational approximation of libjxl (<c>TF_SRGB::DisplayFromEncoded</c>
/// in <c>lib/jxl/cms/transfer_functions-inl.h</c>), which the tools apply to sRGB images: its error, about <c>1e-6</c>,
/// moves SSIMULACRA 2 scores by up to <c>0.02</c>, so the exact function would not give the scores of the tool. It uses
/// fused multiply-adds, as the SIMD code of libjxl does, so it gives the same bits on every platform.
/// </para>
/// </remarks>
internal static class LinearRgbLoader
{
    // libjxl multiplies 8-bit samples by the float 1/255, which is not always the quotient of a division by 255
    private const float InverseMax = 1.0f / 255;

    private static readonly float[] LinearTable = CreateTable();

    /// <summary>
    /// Fills three planes (R, G, B) of the size of the frame; <c>background</c> is the encoded sRGB gray in [0, 1] behind
    /// transparent pixels.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static void Load(PixelSource source, float background, FloatPlane[] planes, CancellationToken cancellationToken)
    {
        var linearBackground = ToLinear(background);
        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = source.GetRow(y);
            var red = planes[0].GetRow(y);
            var green = planes[1].GetRow(y);
            var blue = planes[2].GetRow(y);
            for (var x = 0; x < row.Length; x++)
            {
                var pixel = row[x];
                switch (pixel.A)
                {
                    case 255:
                        red[x] = LinearTable[pixel.R];
                        green[x] = LinearTable[pixel.G];
                        blue[x] = LinearTable[pixel.B];
                        break;

                    case 0:
                        red[x] = linearBackground;
                        green[x] = linearBackground;
                        blue[x] = linearBackground;
                        break;

                    default:
                        var alpha = pixel.A * InverseMax;
                        var backgroundWeight = (1f - alpha) * background;
                        red[x] = ToLinear(alpha * (pixel.R * InverseMax) + backgroundWeight);
                        green[x] = ToLinear(alpha * (pixel.G * InverseMax) + backgroundWeight);
                        blue[x] = ToLinear(alpha * (pixel.B * InverseMax) + backgroundWeight);
                        break;
                }
            }
        }
    }

    /// <summary>The sRGB to linear conversion of libjxl: a rational polynomial of degree 4 above the linear segment.</summary>
    private static float ToLinear(float x)
    {
        if (!(x > 0.04045f))
            return x * (1.0f / 12.92f);

        var p = MathF.FusedMultiplyAdd(8.210152774e-01f, x, 7.961564959e-01f);
        var q = MathF.FusedMultiplyAdd(6.521209011e-03f, x, -5.512498495e-02f);
        p = MathF.FusedMultiplyAdd(p, x, 1.624820318e-01f);
        q = MathF.FusedMultiplyAdd(q, x, 4.987528350e-01f);
        p = MathF.FusedMultiplyAdd(p, x, 1.043637593e-02f);
        q = MathF.FusedMultiplyAdd(q, x, 1.076976492e+00f);
        p = MathF.FusedMultiplyAdd(p, x, 2.200248328e-04f);
        q = MathF.FusedMultiplyAdd(q, x, 2.631846970e-01f);
        return p / q;
    }

    private static float[] CreateTable()
    {
        var table = new float[256];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = ToLinear(i * InverseMax);
        }

        return table;
    }
}
