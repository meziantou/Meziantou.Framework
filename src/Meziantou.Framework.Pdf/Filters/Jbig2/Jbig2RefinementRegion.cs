namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The generic refinement region decoding procedure (T.88, 6.3): a bitmap coded relative to a reference bitmap.</summary>
internal static class Jbig2RefinementRegion
{
    public static int GetContextCount(int template) => template == 0 ? 1 << 13 : 1 << 10;

    public static (int X, int Y)[] GetDefaultAdaptivePixels() => [(-1, -1), (-1, -1)];

    /// <summary>Decodes a refined bitmap (6.3.5).</summary>
    /// <param name="decoder">The arithmetic decoder.</param>
    /// <param name="contexts">The contexts of the template, kept between the regions that share them.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="template">The template, 0 or 1.</param>
    /// <param name="reference">The reference bitmap.</param>
    /// <param name="referenceDx">The horizontal offset of the reference (GRREFERENCEDX).</param>
    /// <param name="referenceDy">The vertical offset of the reference (GRREFERENCEDY).</param>
    /// <param name="typicalPrediction">Whether typical prediction is used (TPGRON).</param>
    /// <param name="adaptivePixels">The two adaptive template pixels of template 0: one in the bitmap, one in the reference.</param>
    /// <param name="budget">The budget.</param>
    public static Jbig2Bitmap Decode(Jbig2ArithmeticDecoder decoder, byte[] contexts, int width, int height, int template, Jbig2Bitmap reference, int referenceDx, int referenceDy, bool typicalPrediction, (int X, int Y)[] adaptivePixels, Jbig2Budget budget)
    {
        budget.ChargeWork(width, height);
        var bitmap = new Jbig2Bitmap(width, height, budget);
        var typical = 0;
        for (var y = 0; y < height; y++)
        {
            if (typicalPrediction)
            {
                // The context where only the pixel of the reference under the pixel is 1 (6.3.5.6)
                typical ^= decoder.Decode(contexts, template == 0 ? 1 << 8 : 1 << 7);
            }

            for (var x = 0; x < width; x++)
            {
                if (typical != 0)
                {
                    // Typical prediction: a pixel whose 3x3 neighborhood in the reference is of one color is that color (6.3.5.6)
                    var rx = x - referenceDx;
                    var ry = y - referenceDy;
                    var value = reference.GetPixel(rx, ry);
                    var uniform = true;
                    for (var dy = -1; dy <= 1 && uniform; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (reference.GetPixel(rx + dx, ry + dy) != value)
                            {
                                uniform = false;
                                break;
                            }
                        }
                    }

                    if (uniform)
                    {
                        bitmap.SetPixel(x, y, value);
                        continue;
                    }
                }

                var context = GetContext(bitmap, reference, x, y, template, referenceDx, referenceDy, adaptivePixels);
                if (decoder.Decode(contexts, context) != 0)
                {
                    bitmap.SetPixel(x, y, 1);
                }
            }
        }

        return bitmap;
    }

    /// <summary>Gets the context of a pixel from its template in the bitmap and in the reference (6.3.5.3, figures 12 and 13).</summary>
    private static int GetContext(Jbig2Bitmap bitmap, Jbig2Bitmap reference, int x, int y, int template, int referenceDx, int referenceDy, (int X, int Y)[] at)
    {
        int Current(int dx, int dy) => bitmap.GetPixel(x + dx, y + dy);
        int Reference(int dx, int dy) => reference.GetPixel(x - referenceDx + dx, y - referenceDy + dy);

        if (template == 0)
        {
            return Current(-1, 0)
                | (Current(1, -1) << 1)
                | (Current(0, -1) << 2)
                | (Current(at[0].X, at[0].Y) << 3)
                | (Reference(1, 1) << 4)
                | (Reference(0, 1) << 5)
                | (Reference(-1, 1) << 6)
                | (Reference(1, 0) << 7)
                | (Reference(0, 0) << 8)
                | (Reference(-1, 0) << 9)
                | (Reference(1, -1) << 10)
                | (Reference(0, -1) << 11)
                | (Reference(at[1].X, at[1].Y) << 12);
        }

        return Current(-1, 0)
            | (Current(1, -1) << 1)
            | (Current(0, -1) << 2)
            | (Current(-1, -1) << 3)
            | (Reference(1, 1) << 4)
            | (Reference(0, 1) << 5)
            | (Reference(1, 0) << 6)
            | (Reference(0, 0) << 7)
            | (Reference(-1, 0) << 8)
            | (Reference(0, -1) << 9);
    }
}
