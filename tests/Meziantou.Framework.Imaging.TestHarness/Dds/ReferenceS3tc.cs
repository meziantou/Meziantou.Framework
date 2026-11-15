using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Dds;

/// <summary>
/// The texels of the S3TC (BC1, BC2, BC3) and RGTC (BC4, BC5) blocks as the Khronos Data Format Specification 1.3.1
/// defines them (sections 18 and 19): rational numbers, here as a numerator over a denominator, rounded once to the
/// precision of the output (8 bits for the unsigned formats, multiples of 1/127 for the signed ones), halves up. It is the
/// hand-computed reference of the fixtures of these formats and never uses the library under test.
/// </summary>
public static class ReferenceS3tc
{
    /// <summary>Decodes the blocks of a surface into RGBA values: 8-bit samples for BC1 to BC5, multiples of 1/127 for the signed formats.</summary>
    /// <param name="blocks">The blocks of the surface, row by row.</param>
    /// <param name="width">The width of the surface.</param>
    /// <param name="height">The height of the surface.</param>
    /// <param name="format">BC1, BC2, BC3, BC4, BC4S, BC5 or BC5S.</param>
    /// <returns>Four real numbers per pixel, the rows from the top; one channel is gray, two are red and green with a blue of 0.</returns>
    public static double[] Decode(ReadOnlySpan<byte> blocks, int width, int height, string format)
    {
        var blockLength = format is "BC1" or "BC4" or "BC4S" ? 8 : 16;
        var blocksAcross = (width + 3) / 4;
        var result = new double[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var block = blocks.Slice((((y / 4) * blocksAcross) + (x / 4)) * blockLength, blockLength);
                var texel = (x % 4) + (4 * (y % 4));
                var rgba = result.AsSpan(((y * width) + x) * 4, 4);
                switch (format)
                {
                    case "BC1":
                        Color(block, texel, threeColor: true, rgba);
                        break;
                    case "BC2":
                        Color(block[8..], texel, threeColor: false, rgba);
                        rgba[3] = ((BinaryPrimitives.ReadUInt64LittleEndian(block) >> (4 * texel)) & 0xF) / 15.0;
                        break;
                    case "BC3":
                        Color(block[8..], texel, threeColor: false, rgba);
                        rgba[3] = Channel(block, texel, signed: false);
                        break;
                    case "BC4" or "BC4S":
                        var value = Channel(block, texel, format == "BC4S");
                        rgba[0] = rgba[1] = rgba[2] = value;
                        rgba[3] = 1;
                        break;
                    case "BC5" or "BC5S":
                        rgba[0] = Channel(block, texel, format == "BC5S");
                        rgba[1] = Channel(block[8..], texel, format == "BC5S");
                        rgba[2] = 0;
                        rgba[3] = 1;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(format), format, "Not an S3TC or RGTC format.");
                }
            }
        }

        return result;
    }

    /// <summary>Gets the number of texels of the surface whose value is exactly halfway between two 8-bit values (BC1 three-color midpoints).</summary>
    public static int CountTies(ReadOnlySpan<byte> blocks, int width, int height, string format)
    {
        if (format is not ("BC1" or "BC2" or "BC3"))
            return 0;

        var blockLength = format == "BC1" ? 8 : 16;
        var blocksAcross = (width + 3) / 4;
        var ties = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var block = blocks.Slice((((y / 4) * blocksAcross) + (x / 4)) * blockLength, blockLength)[(format == "BC1" ? 0 : 8)..];
                var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
                var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
                var code = (BinaryPrimitives.ReadUInt32LittleEndian(block[4..]) >> (2 * ((x % 4) + (4 * (y % 4))))) & 3;
                if (format != "BC1" || c0 > c1 || code != 2)
                    continue;

                // (a + b) × 255 / (2 × max) is a half when (a + b) × 255 is an odd multiple of max
                foreach (var (shift, bits) in new[] { (11, 5), (5, 6), (0, 5) })
                {
                    var max = (1 << bits) - 1;
                    var sum = ((c0 >> shift) & max) + ((c1 >> shift) & max);
                    if ((sum * 255) % (2 * max) == max)
                        ties++;
                }
            }
        }

        return ties;
    }

    private static void Color(ReadOnlySpan<byte> block, int texel, bool threeColor, Span<double> rgba)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var code = (int)(BinaryPrimitives.ReadUInt32LittleEndian(block[4..]) >> (2 * texel)) & 3;
        var fourColors = !threeColor || c0 > c1;
        rgba[3] = 1;
        if (!fourColors && code == 3)
        {
            // Transparent black
            rgba[0] = rgba[1] = rgba[2] = rgba[3] = 0;
            return;
        }

        foreach (var (channel, shift, bits) in new[] { (0, 11, 5), (1, 5, 6), (2, 0, 5) })
        {
            var max = (1 << bits) - 1;
            var a = (c0 >> shift) & max;
            var b = (c1 >> shift) & max;

            // The value as a numerator over a denominator: RGB0, RGB1, the thirds, or the half
            var (numerator, denominator) = (fourColors, code) switch
            {
                (_, 0) => (a, max),
                (_, 1) => (b, max),
                (true, 2) => ((2 * a) + b, 3 * max),
                (true, 3) => (a + (2 * b), 3 * max),
                _ => (a + b, 2 * max),
            };

            rgba[channel] = RoundHalfUp(numerator * 255, denominator) / 255.0;
        }
    }

    /// <summary>An RGTC channel (section 19, Table 108): an 8-bit fraction, or a multiple of 1/127 for a signed channel.</summary>
    private static double Channel(ReadOnlySpan<byte> block, int texel, bool signed)
    {
        int red0 = signed ? (sbyte)block[0] : block[0];
        int red1 = signed ? (sbyte)block[1] : block[1];
        var scale = signed ? 127 : 255;
        var v0 = signed ? Math.Max(red0, -127) : red0;
        var v1 = signed ? Math.Max(red1, -127) : red1;
        var code = (int)((BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16) >> (3 * texel)) & 7;
        int numerator;
        int denominator;
        if (code == 0)
        {
            (numerator, denominator) = (v0, 1);
        }
        else if (code == 1)
        {
            (numerator, denominator) = (v1, 1);
        }
        else if (red0 > red1)
        {
            (numerator, denominator) = (((8 - code) * v0) + ((code - 1) * v1), 7);
        }
        else if (code < 6)
        {
            (numerator, denominator) = (((6 - code) * v0) + ((code - 1) * v1), 5);
        }
        else
        {
            (numerator, denominator) = (code == 6 ? (signed ? -127 : 0) : scale, 1);
        }

        var rounded = numerator >= 0 ? RoundHalfUp(numerator, denominator) : -RoundHalfUp(-numerator, denominator);
        return (double)rounded / scale;
    }

    private static int RoundHalfUp(int numerator, int denominator) => ((2 * numerator) + denominator) / (2 * denominator);
}
