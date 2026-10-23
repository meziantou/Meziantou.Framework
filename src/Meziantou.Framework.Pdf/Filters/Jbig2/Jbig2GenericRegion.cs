using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The generic region decoding procedure (T.88, 6.2): a bitmap coded with the MQ coder and a template of neighbor pixels, or with MMR.</summary>
internal static class Jbig2GenericRegion
{
    // The contexts of the bit that toggles typical prediction (6.2.5.7), per template
    private static readonly int[] TypicalPredictionContexts = [0x9B25, 0x0795, 0x00E5, 0x0195];

    public static int GetContextCount(int template) => template switch
    {
        0 => 1 << 16,
        1 => 1 << 13,
        _ => 1 << 10,
    };

    /// <summary>Gets the default adaptive template pixels of a template (6.2.5.3).</summary>
    public static (int X, int Y)[] GetDefaultAdaptivePixels(int template) => template == 0 ? [(3, -1), (-3, -1), (2, -2), (-2, -2)] : template == 1 ? [(3, -1)] : [(2, -1)];

    /// <summary>Decodes a bitmap with the MQ coder (6.2.5).</summary>
    /// <param name="decoder">The arithmetic decoder.</param>
    /// <param name="contexts">The contexts of the template (<see cref="GetContextCount"/>), kept between the regions that share them.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="template">The template, 0 to 3.</param>
    /// <param name="typicalPrediction">Whether the rows may repeat the row above (TPGDON).</param>
    /// <param name="skip">The pixels that are not coded and are 0, or <see langword="null"/>.</param>
    /// <param name="adaptivePixels">The positions of the adaptive template pixels.</param>
    /// <param name="budget">The budget.</param>
    public static Jbig2Bitmap Decode(Jbig2ArithmeticDecoder decoder, byte[] contexts, int width, int height, int template, bool typicalPrediction, Jbig2Bitmap? skip, (int X, int Y)[] adaptivePixels, Jbig2Budget budget)
    {
        budget.ChargeWork((long)width * height);
        var bitmap = new Jbig2Bitmap(width, height, budget);
        var typical = 0;
        for (var y = 0; y < height; y++)
        {
            if (typicalPrediction)
            {
                typical ^= decoder.Decode(contexts, TypicalPredictionContexts[template]);
                if (typical != 0)
                {
                    // The row is the row above, or white for the first row
                    if (y > 0)
                    {
                        bitmap.CopyRow(y - 1, y);
                    }

                    continue;
                }
            }

            DecodeRow(decoder, contexts, bitmap, y, template, skip, adaptivePixels);
        }

        return bitmap;
    }

    /// <summary>
    /// Decodes a bitmap coded with MMR (6.2.6): Group 4 facsimile coding, 1 for black. The rows are decoded by the CCITT
    /// decoder of the TIFF codec of Meziantou.Framework.Imaging.
    /// </summary>
    /// <returns>The bitmap, and the bytes the coding used (an end-of-block code included).</returns>
    public static (Jbig2Bitmap Bitmap, int Length) DecodeMmr(byte[] data, int offset, int length, int width, int height, Jbig2Budget budget)
    {
        budget.ChargeWork((long)width * height);
        var bitmap = new Jbig2Bitmap(width, height, budget);
        if (width == 0 || height == 0)
            return (bitmap, 0);

        var changeListLength = (int)TiffCcittDecoder.GetChangeListLength(width);
        var reference = new int[changeListLength];
        var current = new int[changeListLength];
        var reader = new TiffBitReader(data.AsSpan(offset, length));
        TiffCcittDecoder.Terminate(reference, 0, width);
        for (var y = 0; y < height; y++)
        {
            var error = TiffCcittDecoder.DecodeTwoDimensionalRow(ref reader, reference, current, width, out var count);
            if (error is not null || reader.IsPastEnd)
            {
                // Damaged data: the rows decoded before it are kept
                break;
            }

            TiffCcittDecoder.Terminate(current, count, width);
            TiffCcittDecoder.Paint(current, count, bitmap.GetRow(y), width);
            (reference, current) = (current, reference);
        }

        // An end-of-block code may follow (two end-of-line codes)
        for (var i = 0; i < 2 && TiffCcittDecoder.TrySkipEndOfLine(ref reader); i++)
        {
        }

        reader.AlignToByte();
        return (bitmap, (int)Math.Min(length, reader.Position / 8));
    }

    /// <summary>
    /// Decodes a row. The pixels of the template that are at fixed positions slide in three windows, one per row, from
    /// which the context is assembled; the adaptive pixels are read where they are, unless they are at their default
    /// positions, which the windows cover.
    /// </summary>
    private static void DecodeRow(Jbig2ArithmeticDecoder decoder, byte[] contexts, Jbig2Bitmap bitmap, int y, int template, Jbig2Bitmap? skip, (int X, int Y)[] at)
    {
        var data = bitmap.Data;
        var width = bitmap.Width;
        var stride = bitmap.Stride;
        var row = y * stride;
        var row1 = y >= 1 ? (y - 1) * stride : -1;
        var row2 = y >= 2 ? (y - 2) * stride : -1;

        // The windows of the rows above: the range of their positions relative to x, and the shift of each in the context
        var (left1, right1, left2, right2, shift1, shift2, currentLength) = template switch
        {
            0 => (-3, 3, -2, 2, 4, 11, 4),
            1 => (-2, 3, -1, 2, 3, 9, 3),
            2 => (-2, 2, -1, 1, 2, 7, 2),
            _ => (-3, 2, 0, -1, 4, 0, 4),
        };
        var defaults = GetDefaultAdaptivePixels(template);
        var isDefault = at.AsSpan().SequenceEqual(defaults);
        var mask1 = (1 << (right1 - left1 + 1)) - 1;
        var mask2 = right2 >= left2 ? (1 << (right2 - left2 + 1)) - 1 : 0;
        var maskCurrent = (1 << currentLength) - 1;
        var window1 = 0;
        var window2 = 0;
        for (var dx = left1; dx <= right1; dx++)
        {
            window1 = (window1 << 1) | Bit(data, row1, dx, width);
        }

        for (var dx = left2; dx <= right2; dx++)
        {
            window2 = (window2 << 1) | Bit(data, row2, dx, width);
        }

        // The adaptive pixels at their default positions are in the windows; the others are replaced
        var fixedMask = isDefault ? -1 : ~GetAdaptiveMask(template);
        var current = 0;
        for (var x = 0; x < width; x++)
        {
            var value = 0;
            if (skip is null || skip.GetPixel(x, y) == 0)
            {
                var context = (current | (window1 << shift1) | (window2 << shift2)) & fixedMask;
                if (!isDefault)
                {
                    context |= GetAdaptiveBits(bitmap, x, y, template, at);
                }

                value = decoder.Decode(contexts, context);
                if (value != 0)
                {
                    data[row + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }

            current = ((current << 1) | value) & maskCurrent;
            window1 = ((window1 << 1) | Bit(data, row1, x + 1 + right1, width)) & mask1;
            window2 = ((window2 << 1) | Bit(data, row2, x + 1 + right2, width)) & mask2;
        }
    }

    private static int Bit(byte[] data, int row, int x, int width) => row >= 0 && (uint)x < (uint)width ? (data[row + (x >> 3)] >> (7 - (x & 7))) & 1 : 0;

    /// <summary>Gets the bits of the context that are adaptive pixels.</summary>
    private static int GetAdaptiveMask(int template) => template switch
    {
        0 => (1 << 4) | (1 << 10) | (1 << 11) | (1 << 15),
        1 => 1 << 3,
        2 => 1 << 2,
        _ => 1 << 4,
    };

    private static int GetAdaptiveBits(Jbig2Bitmap bitmap, int x, int y, int template, (int X, int Y)[] at)
    {
        int Pixel(int index) => bitmap.GetPixel(x + at[index].X, y + at[index].Y);

        return template switch
        {
            0 => (Pixel(0) << 4) | (Pixel(1) << 10) | (Pixel(2) << 11) | (Pixel(3) << 15),
            1 => Pixel(0) << 3,
            2 => Pixel(0) << 2,
            _ => Pixel(0) << 4,
        };
    }
}
