using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The WebP lossless transforms (WebP Lossless Bitstream specification, section 4) on ARGB pixels (<c>0xAARRGGBB</c>):
/// the 14 spatial predictors, the color transform, subtract green and color indexing with pixel bundling. Shared by the
/// decoder (inverse transforms, in place) and the encoder (forward transforms).
/// </summary>
internal static class Vp8LTransforms
{
    /// <summary>The prediction of the top-left pixel and of mode 0 (and of the undefined modes 14 and 15): opaque black.</summary>
    public const uint Black = 0xFF000000u;

    /// <summary>Predicts a pixel with one of the 14 modes from its decoded neighbors (left, top, top-right, top-left).</summary>
    /// <param name="mode">The mode (the low four bits of the predictor image green component); 14 and 15 behave like 0.</param>
    /// <param name="left">The left pixel.</param>
    /// <param name="top">The top pixel.</param>
    /// <param name="topRight">The top-right pixel.</param>
    /// <param name="topLeft">The top-left pixel.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Predict(int mode, uint left, uint top, uint topRight, uint topLeft) => mode switch
    {
        1 => left,
        2 => top,
        3 => topRight,
        4 => topLeft,
        5 => Average2(Average2(left, topRight), top),
        6 => Average2(left, topLeft),
        7 => Average2(left, top),
        8 => Average2(topLeft, top),
        9 => Average2(top, topRight),
        10 => Average2(Average2(left, topLeft), Average2(top, topRight)),
        11 => Select(left, top, topLeft),
        12 => ClampAddSubtractFull(left, top, topLeft),
        13 => ClampAddSubtractHalf(Average2(left, top), topLeft),
        _ => Black,
    };

    /// <summary>Reverses the predictor transform in place: each residual becomes the residual plus the prediction from the already reconstructed pixels.</summary>
    /// <remarks>
    /// Every mode has its own loop. The modes that do not read the left pixel (0, 2, 3, 4, 8, 9 and the undefined 14 and 15)
    /// only read the row above, which is final: they are computed 4 pixels at a time.
    /// </remarks>
    public static void InversePredictor(Span<uint> pixels, int width, int height, int sizeBits, ReadOnlySpan<uint> modes, int modesWidth)
    {
        if (pixels.IsEmpty)
            return;

        var useVectors = Vector128.IsHardwareAccelerated;

        // First row: the top-left pixel predicts black, the others their left neighbor
        pixels[0] = Vp8LDecoder.AddPixels(pixels[0], Black);
        AddLeft(pixels, 1, width);
        var tileWidth = 1 << sizeBits;
        for (var y = 1; y < height; y++)
        {
            // The row and the row above, plus one pixel: on the last column the top-right pixel is, in memory order, the
            // first pixel of the current row
            var row = pixels.Slice(y * width, width);
            var above = pixels.Slice((y - 1) * width, width + 1);

            // First column: the top neighbor
            row[0] = Vp8LDecoder.AddPixels(row[0], above[0]);
            var modeRow = modes.Slice((y >> sizeBits) * modesWidth, modesWidth);
            for (var tile = 0; tile < modesWidth; tile++)
            {
                var start = Math.Max(1, tile * tileWidth);
                var end = Math.Min(width, (tile + 1) * tileWidth);
                if (start >= end)
                    continue;

                var mode = (int)((modeRow[tile] >> 8) & 0xF);
                switch (mode)
                {
                    case 1:
                        AddLeft(row, start, end);
                        break;
                    case 2:
                        AddAbove(row, above, start, end, 0, useVectors);
                        break;
                    case 3:
                        AddAbove(row, above, start, end, 1, useVectors);
                        break;
                    case 4:
                        AddAbove(row, above, start, end, -1, useVectors);
                        break;
                    case 5:
                        PredictMode5(row, above, start, end);
                        break;
                    case 6:
                        PredictMode6(row, above, start, end);
                        break;
                    case 7:
                        PredictMode7(row, above, start, end);
                        break;
                    case 8:
                        AddAboveAverage(row, above, start, end, -1, useVectors);
                        break;
                    case 9:
                        AddAboveAverage(row, above, start, end, 1, useVectors);
                        break;
                    case 10:
                        PredictMode10(row, above, start, end);
                        break;
                    case 11:
                        PredictMode11(row, above, start, end);
                        break;
                    case 12:
                        PredictMode12(row, above, start, end);
                        break;
                    case 13:
                        PredictMode13(row, above, start, end);
                        break;
                    default:
                        AddBlack(row, start, end, useVectors);
                        break;
                }
            }
        }
    }

    private static void AddLeft(Span<uint> row, int start, int end)
    {
        if (start >= end)
            return;

        var left = row[start - 1];
        foreach (ref var pixel in row[start..end])
        {
            left = Vp8LDecoder.AddPixels(pixel, left);
            pixel = left;
        }
    }

    private static void AddBlack(Span<uint> row, int start, int end, bool useVectors)
    {
        var x = start;
        if (useVectors)
        {
            var black = Vector128.Create(Black).AsByte();
            for (; x <= end - Vector128<uint>.Count; x += Vector128<uint>.Count)
            {
                var target = row.Slice(x, Vector128<uint>.Count);
                (Vector128.Create<uint>(target).AsByte() + black).AsUInt32().CopyTo(target);
            }
        }

        for (; x < end; x++)
        {
            row[x] = Vp8LDecoder.AddPixels(row[x], Black);
        }
    }

    // Modes 2 (top), 3 (top-right) and 4 (top-left): the pixel of the row above at the column plus offset
    private static void AddAbove(Span<uint> row, ReadOnlySpan<uint> above, int start, int end, int offset, bool useVectors)
    {
        var x = start;
        if (useVectors)
        {
            for (; x <= end - Vector128<uint>.Count; x += Vector128<uint>.Count)
            {
                var target = row.Slice(x, Vector128<uint>.Count);
                var prediction = Vector128.Create(above.Slice(x + offset, Vector128<uint>.Count));
                (Vector128.Create<uint>(target).AsByte() + prediction.AsByte()).AsUInt32().CopyTo(target);
            }
        }

        for (; x < end; x++)
        {
            row[x] = Vp8LDecoder.AddPixels(row[x], above[x + offset]);
        }
    }

    // Modes 8 (average of top-left and top) and 9 (average of top and top-right): the top pixel averaged with the pixel of
    // the row above at the column plus offset
    private static void AddAboveAverage(Span<uint> row, ReadOnlySpan<uint> above, int start, int end, int offset, bool useVectors)
    {
        var x = start;
        if (useVectors)
        {
            var mask = Vector128.Create(0xFEFEFEFEu);
            for (; x <= end - Vector128<uint>.Count; x += Vector128<uint>.Count)
            {
                var target = row.Slice(x, Vector128<uint>.Count);
                var top = Vector128.Create(above.Slice(x, Vector128<uint>.Count));
                var other = Vector128.Create(above.Slice(x + offset, Vector128<uint>.Count));

                // Average2 of every lane, with the same formula (the operand order does not matter)
                var prediction = Vector128.ShiftRightLogical((top ^ other) & mask, 1) + (top & other);
                (Vector128.Create<uint>(target).AsByte() + prediction.AsByte()).AsUInt32().CopyTo(target);
            }
        }

        for (; x < end; x++)
        {
            row[x] = Vp8LDecoder.AddPixels(row[x], Average2(above[x + offset], above[x]));
        }
    }

    private static void PredictMode5(Span<uint> row, ReadOnlySpan<uint> above, int start, int end)
    {
        var left = row[start - 1];
        for (var x = start; x < end; x++)
        {
            left = Vp8LDecoder.AddPixels(row[x], Average2(Average2(left, above[x + 1]), above[x]));
            row[x] = left;
        }
    }

    private static void PredictMode6(Span<uint> row, ReadOnlySpan<uint> above, int start, int end)
    {
        var left = row[start - 1];
        for (var x = start; x < end; x++)
        {
            left = Vp8LDecoder.AddPixels(row[x], Average2(left, above[x - 1]));
            row[x] = left;
        }
    }

    private static void PredictMode7(Span<uint> row, ReadOnlySpan<uint> above, int start, int end)
    {
        var left = row[start - 1];
        for (var x = start; x < end; x++)
        {
            left = Vp8LDecoder.AddPixels(row[x], Average2(left, above[x]));
            row[x] = left;
        }
    }

    private static void PredictMode10(Span<uint> row, ReadOnlySpan<uint> above, int start, int end)
    {
        var left = row[start - 1];
        for (var x = start; x < end; x++)
        {
            left = Vp8LDecoder.AddPixels(row[x], Average2(Average2(left, above[x - 1]), Average2(above[x], above[x + 1])));
            row[x] = left;
        }
    }

    private static void PredictMode11(Span<uint> row, ReadOnlySpan<uint> above, int start, int end)
    {
        var left = row[start - 1];
        for (var x = start; x < end; x++)
        {
            left = Vp8LDecoder.AddPixels(row[x], SelectUnrolled(left, above[x], above[x - 1]));
            row[x] = left;
        }
    }

    private static void PredictMode12(Span<uint> row, ReadOnlySpan<uint> above, int start, int end)
    {
        var left = row[start - 1];
        for (var x = start; x < end; x++)
        {
            left = Vp8LDecoder.AddPixels(row[x], ClampAddSubtractFullUnrolled(left, above[x], above[x - 1]));
            row[x] = left;
        }
    }

    private static void PredictMode13(Span<uint> row, ReadOnlySpan<uint> above, int start, int end)
    {
        var left = row[start - 1];
        for (var x = start; x < end; x++)
        {
            left = Vp8LDecoder.AddPixels(row[x], ClampAddSubtractHalfUnrolled(Average2(left, above[x]), above[x - 1]));
            row[x] = left;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SelectUnrolled(uint left, uint top, uint topLeft)
    {
        var distanceToLeft = AbsoluteDifference(top, topLeft, 0) + AbsoluteDifference(top, topLeft, 8) + AbsoluteDifference(top, topLeft, 16) + AbsoluteDifference(top, topLeft, 24);
        var distanceToTop = AbsoluteDifference(left, topLeft, 0) + AbsoluteDifference(left, topLeft, 8) + AbsoluteDifference(left, topLeft, 16) + AbsoluteDifference(left, topLeft, 24);
        return distanceToLeft < distanceToTop ? left : top;

        // Branch-free: the sign of the difference is a mask (Math.Abs compiles to unpredictable branches here)
        static int AbsoluteDifference(uint a, uint b, int shift)
        {
            var difference = (int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF);
            var sign = difference >> 31;
            return (difference + sign) ^ sign;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ClampAddSubtractFullUnrolled(uint a, uint b, uint c)
        => Component(a, b, c, 0) | Component(a, b, c, 8) | Component(a, b, c, 16) | Component(a, b, c, 24);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Component(uint a, uint b, uint c, int shift)
        => ClampToByte((int)((a >> shift) & 0xFF) + (int)((b >> shift) & 0xFF) - (int)((c >> shift) & 0xFF)) << shift;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ClampAddSubtractHalfUnrolled(uint a, uint b)
        => HalfComponent(a, b, 0) | HalfComponent(a, b, 8) | HalfComponent(a, b, 16) | HalfComponent(a, b, 24);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint HalfComponent(uint a, uint b, int shift)
    {
        var ac = (int)((a >> shift) & 0xFF);
        var bc = (int)((b >> shift) & 0xFF);
        return ClampToByte(ac + ((ac - bc) / 2)) << shift;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ClampToByte(int value) => (uint)value <= 255 ? (uint)value : value < 0 ? 0u : 255u;

    /// <summary>The scalar reference of <see cref="InversePredictor"/>: the prediction of every pixel through <see cref="Predict"/>.</summary>
    internal static void InversePredictorScalar(Span<uint> pixels, int width, int height, int sizeBits, ReadOnlySpan<uint> modes, int modesWidth)
    {
        if (pixels.IsEmpty)
            return;

        // First row: the top-left pixel predicts black, the others their left neighbor
        pixels[0] = Vp8LDecoder.AddPixels(pixels[0], Black);
        for (var x = 1; x < width; x++)
        {
            pixels[x] = Vp8LDecoder.AddPixels(pixels[x], pixels[x - 1]);
        }

        var tileWidth = 1 << sizeBits;
        for (var y = 1; y < height; y++)
        {
            // The row and the row above, plus one pixel: on the last column the top-right pixel is, in memory order, the
            // first pixel of the current row
            var row = pixels.Slice(y * width, width);
            var above = pixels.Slice((y - 1) * width, width + 1);

            // First column: the top neighbor
            row[0] = Vp8LDecoder.AddPixels(row[0], above[0]);
            var modeRow = modes.Slice((y >> sizeBits) * modesWidth, modesWidth);
            for (var tile = 0; tile < modesWidth; tile++)
            {
                var start = Math.Max(1, tile * tileWidth);
                var end = Math.Min(width, (tile + 1) * tileWidth);
                if (start >= end)
                    continue;

                var mode = (int)((modeRow[tile] >> 8) & 0xF);
                InversePredictorSegmentScalar(row, above, start, end, mode);
            }
        }
    }

    private static void InversePredictorSegmentScalar(Span<uint> row, ReadOnlySpan<uint> above, int start, int end, int mode)
    {
        switch (mode)
        {
            case 1:
                for (var x = start; x < end; x++)
                {
                    row[x] = Vp8LDecoder.AddPixels(row[x], row[x - 1]);
                }

                break;

            case 2:
                for (var x = start; x < end; x++)
                {
                    row[x] = Vp8LDecoder.AddPixels(row[x], above[x]);
                }

                break;

            case 3:
                for (var x = start; x < end; x++)
                {
                    row[x] = Vp8LDecoder.AddPixels(row[x], above[x + 1]);
                }

                break;

            case 4:
                for (var x = start; x < end; x++)
                {
                    row[x] = Vp8LDecoder.AddPixels(row[x], above[x - 1]);
                }

                break;

            default:
                for (var x = start; x < end; x++)
                {
                    row[x] = Vp8LDecoder.AddPixels(row[x], Predict(mode, row[x - 1], above[x], above[x + 1], above[x - 1]));
                }

                break;
        }
    }

    /// <summary>Reverses the color transform in place.</summary>
    /// <remarks>The pixels of a tile share their transform element: they are computed 4 at a time in 32-bit lanes.</remarks>
    public static void InverseColorTransform(Span<uint> pixels, int width, int height, int sizeBits, ReadOnlySpan<uint> elements, int elementsWidth)
    {
        if (!Vector128.IsHardwareAccelerated)
        {
            InverseColorTransformScalar(pixels, width, height, sizeBits, elements, elementsWidth);
            return;
        }

        var tileWidth = 1 << sizeBits;
        var low = Vector128.Create(0xFFu);
        var alphaGreen = Vector128.Create(0xFF00FF00u);
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width, width);
            var elementRow = elements.Slice((y >> sizeBits) * elementsWidth, elementsWidth);
            for (var tile = 0; tile < elementsWidth; tile++)
            {
                var start = tile * tileWidth;
                var end = Math.Min(width, start + tileWidth);
                if (start >= end)
                    continue;

                var element = elementRow[tile];
                var greenToRed = (sbyte)element;
                var greenToBlue = (sbyte)(element >> 8);
                var redToBlue = (sbyte)(element >> 16);
                var greenToRedVector = Vector128.Create((int)greenToRed);
                var greenToBlueVector = Vector128.Create((int)greenToBlue);
                var redToBlueVector = Vector128.Create((int)redToBlue);
                var x = start;
                for (; x <= end - Vector128<uint>.Count; x += Vector128<uint>.Count)
                {
                    var target = row.Slice(x, Vector128<uint>.Count);
                    var argb = Vector128.Create<uint>(target);

                    // Signed green and new red: arithmetic shifts sign-extend the byte
                    var green = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(argb, 16).AsInt32(), 24);
                    var red = (Vector128.ShiftRightLogical(argb, 16).AsInt32() + Vector128.ShiftRightArithmetic(greenToRedVector * green, 5)) & low.AsInt32();
                    var signedRed = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(red, 24), 24);
                    var blue = (argb.AsInt32() + Vector128.ShiftRightArithmetic(greenToBlueVector * green, 5) + Vector128.ShiftRightArithmetic(redToBlueVector * signedRed, 5)) & low.AsInt32();
                    ((argb & alphaGreen) | Vector128.ShiftLeft(red, 16).AsUInt32() | blue.AsUInt32()).CopyTo(target);
                }

                for (; x < end; x++)
                {
                    row[x] = InverseColorTransformPixel(row[x], greenToRed, greenToBlue, redToBlue);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint InverseColorTransformPixel(uint argb, sbyte greenToRed, sbyte greenToBlue, sbyte redToBlue)
    {
        var green = (sbyte)(argb >> 8);
        var red = (int)((argb >> 16) & 0xFF);
        var blue = (int)(argb & 0xFF);
        red = (red + ColorTransformDelta(greenToRed, green)) & 0xFF;
        blue = (blue + ColorTransformDelta(greenToBlue, green)) & 0xFF;
        blue = (blue + ColorTransformDelta(redToBlue, (sbyte)red)) & 0xFF;
        return (argb & 0xFF00FF00u) | ((uint)red << 16) | (uint)blue;
    }

    /// <summary>The scalar reference of <see cref="InverseColorTransform"/>.</summary>
    internal static void InverseColorTransformScalar(Span<uint> pixels, int width, int height, int sizeBits, ReadOnlySpan<uint> elements, int elementsWidth)
    {
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width, width);
            var elementRow = elements.Slice((y >> sizeBits) * elementsWidth, elementsWidth);
            for (var x = 0; x < row.Length; x++)
            {
                var element = elementRow[x >> sizeBits];
                var greenToRed = (sbyte)element;
                var greenToBlue = (sbyte)(element >> 8);
                var redToBlue = (sbyte)(element >> 16);
                var argb = row[x];
                var green = (sbyte)(argb >> 8);
                var red = (int)((argb >> 16) & 0xFF);
                var blue = (int)(argb & 0xFF);
                red = (red + ColorTransformDelta(greenToRed, green)) & 0xFF;
                blue = (blue + ColorTransformDelta(greenToBlue, green)) & 0xFF;
                blue = (blue + ColorTransformDelta(redToBlue, (sbyte)red)) & 0xFF;
                row[x] = (argb & 0xFF00FF00u) | ((uint)red << 16) | (uint)blue;
            }
        }
    }

    /// <summary>Applies the forward color transform to one pixel (the encoder side of <see cref="InverseColorTransform"/>).</summary>
    public static uint ForwardColorTransform(uint argb, sbyte greenToRed, sbyte greenToBlue, sbyte redToBlue)
    {
        var green = (sbyte)(argb >> 8);
        var red = (int)((argb >> 16) & 0xFF);
        var blue = (int)(argb & 0xFF);
        var newRed = (red - ColorTransformDelta(greenToRed, green)) & 0xFF;
        var newBlue = (blue - ColorTransformDelta(greenToBlue, green)) & 0xFF;
        newBlue = (newBlue - ColorTransformDelta(redToBlue, (sbyte)red)) & 0xFF;
        return (argb & 0xFF00FF00u) | ((uint)newRed << 16) | (uint)newBlue;
    }

    /// <summary>Reverses the subtract green transform in place: green is added to red and blue.</summary>
    public static void AddGreen(Span<uint> pixels)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var redBlueMask = Vector128.Create(0x00FF00FFu);
            var alphaGreen = Vector128.Create(0xFF00FF00u);
            var low = Vector128.Create(0xFFu);
            for (; i <= pixels.Length - Vector128<uint>.Count; i += Vector128<uint>.Count)
            {
                var target = pixels.Slice(i, Vector128<uint>.Count);
                var argb = Vector128.Create<uint>(target);
                var green = Vector128.ShiftRightLogical(argb, 8) & low;
                var redBlue = ((argb & redBlueMask) + (Vector128.ShiftLeft(green, 16) | green)) & redBlueMask;
                ((argb & alphaGreen) | redBlue).CopyTo(target);
            }
        }

        AddGreenScalar(pixels[i..]);
    }

    /// <summary>The scalar reference of <see cref="AddGreen"/>.</summary>
    internal static void AddGreenScalar(Span<uint> pixels)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            var argb = pixels[i];
            var green = (argb >> 8) & 0xFF;
            var redBlue = (argb & 0x00FF00FFu) + ((green << 16) | green);
            pixels[i] = (argb & 0xFF00FF00u) | (redBlue & 0x00FF00FFu);
        }
    }

    /// <summary>Applies the subtract green transform in place (encoder).</summary>
    public static void SubtractGreen(Span<uint> pixels)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            var argb = pixels[i];
            var green = (argb >> 8) & 0xFF;
            var redBlue = (argb & 0x00FF00FFu) + 0x01000100u - ((green << 16) | green);
            pixels[i] = (argb & 0xFF00FF00u) | (redBlue & 0x00FF00FFu);
        }
    }

    /// <summary>
    /// Reverses color indexing in place: the indexes stored in the green component of the <paramref name="packedWidth"/>-wide
    /// rows (bundled several per pixel when <paramref name="widthBits"/> is positive) become the color table entries in
    /// <paramref name="outputWidth"/>-wide rows. Indexes outside the table give transparent black (the table is padded with zeros).
    /// </summary>
    /// <param name="pixels">The buffer, at least <c>outputWidth * height</c> pixels; the packed rows are at its start.</param>
    /// <param name="packedWidth">The width of the packed rows.</param>
    /// <param name="outputWidth">The width of the expanded rows.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="widthBits">The bundling: <c>2^widthBits</c> indexes per packed pixel.</param>
    /// <param name="palette256">The color table padded to 256 entries with transparent black.</param>
    public static void InverseColorIndexing(Span<uint> pixels, int packedWidth, int outputWidth, int height, int widthBits, ReadOnlySpan<uint> palette256)
    {
        // Rows are expanded from the last one, and each row from its last pixel, so that no packed pixel is overwritten before it is read
        var pixelsPerPacked = 1 << widthBits;
        var bitsPerIndex = 8 >> widthBits;
        var indexMask = (1u << bitsPerIndex) - 1;
        for (var y = height - 1; y >= 0; y--)
        {
            var source = y * packedWidth;
            var destination = y * outputWidth;
            for (var x = outputWidth - 1; x >= 0; x--)
            {
                var packed = (pixels[source + (x >> widthBits)] >> 8) & 0xFF;
                var index = (packed >> ((x & (pixelsPerPacked - 1)) * bitsPerIndex)) & indexMask;
                pixels[destination + x] = palette256[(int)index];
            }
        }
    }

    /// <summary><c>(a + b) / 2</c> per component, rounded down.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Average2(uint a, uint b) => (((a ^ b) & 0xFEFEFEFEu) >> 1) + (a & b);

    /// <summary>The color transform delta: <c>(t * c) &gt;&gt; 5</c> on signed 8-bit values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ColorTransformDelta(sbyte t, sbyte c) => (t * c) >> 5;

    private static uint Select(uint left, uint top, uint topLeft)
    {
        // Manhattan distances between the gradient estimate L + T - TL and each of L and T: |T - TL| and |L - TL| summed per component
        var distanceToLeft = AbsoluteDifferenceSum(top, topLeft);
        var distanceToTop = AbsoluteDifferenceSum(left, topLeft);
        return distanceToLeft < distanceToTop ? left : top;
    }

    private static int AbsoluteDifferenceSum(uint a, uint b)
    {
        var sum = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            sum += Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF));
        }

        return sum;
    }

    private static uint ClampAddSubtractFull(uint a, uint b, uint c)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var value = (int)((a >> shift) & 0xFF) + (int)((b >> shift) & 0xFF) - (int)((c >> shift) & 0xFF);
            result |= (uint)Math.Clamp(value, 0, 255) << shift;
        }

        return result;
    }

    private static uint ClampAddSubtractHalf(uint a, uint b)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var ac = (int)((a >> shift) & 0xFF);
            var bc = (int)((b >> shift) & 0xFF);
            result |= (uint)Math.Clamp(ac + ((ac - bc) / 2), 0, 255) << shift;
        }

        return result;
    }
}
