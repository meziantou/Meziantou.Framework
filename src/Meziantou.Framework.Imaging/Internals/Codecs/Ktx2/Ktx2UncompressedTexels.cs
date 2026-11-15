using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes rows of uncompressed texels (the RGBSDA formats of <see cref="Ktx2VkFormats"/>) into RGBA rows: each sample is
/// read at its bit offset in the little-endian texel and converted to the precision of the format.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>8-bit and 16-bit components are copied; components of 1 to 6 bits become 8-bit samples and those of 10 bits 16-bit samples, each the nearest value of its fraction, <c>round(v × (2^n - 1) / max)</c>, which is exact and reversible.</description></item>
/// <item><description>16-bit and 32-bit floats are copied; the 11-bit and 10-bit unsigned floats have the 5-bit exponent and the bias of a half, so they become the half of the same exponent and mantissa.</description></item>
/// <item><description>A shared-exponent texel (E5B9G9R9) is <c>m × 2^(e - 24)</c> for each mantissa: a multiple of the smallest subnormal half below 65,408, exact as a half.</description></item>
/// <item><description>The channels a format lacks are 0, and alpha is 1.</description></item>
/// </list>
/// </remarks>
internal static class Ktx2UncompressedTexels
{
    public static void DecodeRow(Ktx2VkFormat format, ReadOnlySpan<byte> source, int width, Span<byte> destination)
    {
        switch (format.Precision)
        {
            case Ktx2OutputPrecision.Byte:
                DecodeBytes(format, source, width, destination);
                break;
            case Ktx2OutputPrecision.UInt16:
                DecodeUInt16(format, source, width, unsafe(MemoryMarshal.Cast<byte, ushort>(destination)));
                break;
            case Ktx2OutputPrecision.Half:
                DecodeHalves(format, source, width, unsafe(MemoryMarshal.Cast<byte, ushort>(destination)));
                break;
            default:
                DecodeFloats(format, source, width, unsafe(MemoryMarshal.Cast<byte, float>(destination)));
                break;
        }
    }

    private static int GetComponent(int channel) => channel == Ktx2Format.ChannelAlpha ? 3 : channel;

    private static void DecodeBytes(Ktx2VkFormat format, ReadOnlySpan<byte> source, int width, Span<byte> destination)
    {
        var length = format.BytesPerBlock;
        var samples = format.Samples;
        var alpha = (format.ChannelMask & 8) == 0 ? (byte)255 : (byte)0;
        if (format.TypeSize == 1)
        {
            // Arrays of 8-bit components
            for (var x = 0; x < width; x++)
            {
                var texel = source.Slice(x * length, length);
                var pixel = destination.Slice(x * 4, 4);
                pixel[0] = 0;
                pixel[1] = 0;
                pixel[2] = 0;
                pixel[3] = alpha;
                foreach (var sample in samples)
                {
                    pixel[GetComponent(sample.Channel)] = texel[sample.BitOffset >> 3];
                }
            }

            return;
        }

        // Packed components of 1 to 8 bits in a 16-bit or 32-bit word
        for (var x = 0; x < width; x++)
        {
            var word = ReadWord(source.Slice(x * length, length));
            var pixel = destination.Slice(x * 4, 4);
            pixel[0] = 0;
            pixel[1] = 0;
            pixel[2] = 0;
            pixel[3] = alpha;
            foreach (var sample in samples)
            {
                var max = (1u << sample.BitLength) - 1;
                var value = (word >> sample.BitOffset) & max;
                pixel[GetComponent(sample.Channel)] = (byte)(((value * 255) + (max / 2)) / max);
            }
        }
    }

    private static void DecodeUInt16(Ktx2VkFormat format, ReadOnlySpan<byte> source, int width, Span<ushort> destination)
    {
        var length = format.BytesPerBlock;
        var samples = format.Samples;
        var alpha = (format.ChannelMask & 8) == 0 ? ushort.MaxValue : (ushort)0;
        for (var x = 0; x < width; x++)
        {
            var texel = source.Slice(x * length, length);
            var pixel = destination.Slice(x * 4, 4);
            pixel[0] = 0;
            pixel[1] = 0;
            pixel[2] = 0;
            pixel[3] = alpha;
            if (format.TypeSize == 2)
            {
                foreach (var sample in samples)
                {
                    pixel[GetComponent(sample.Channel)] = BinaryPrimitives.ReadUInt16LittleEndian(texel[(sample.BitOffset >> 3)..]);
                }

                continue;
            }

            // 10-bit and 2-bit components of a 32-bit word
            var word = ReadWord(texel);
            foreach (var sample in samples)
            {
                var max = (1u << sample.BitLength) - 1;
                var value = (word >> sample.BitOffset) & max;
                pixel[GetComponent(sample.Channel)] = (ushort)(((value * 65535) + (max / 2)) / max);
            }
        }
    }

    private static void DecodeHalves(Ktx2VkFormat format, ReadOnlySpan<byte> source, int width, Span<ushort> destination)
    {
        var length = format.BytesPerBlock;
        var one = BitConverter.HalfToUInt16Bits(Half.One);
        for (var x = 0; x < width; x++)
        {
            var texel = source.Slice(x * length, length);
            var pixel = destination.Slice(x * 4, 4);
            pixel[0] = 0;
            pixel[1] = 0;
            pixel[2] = 0;
            pixel[3] = one;
            switch (format.NumericType)
            {
                case Ktx2NumericType.Sfloat:
                    foreach (var sample in format.Samples)
                    {
                        pixel[GetComponent(sample.Channel)] = BinaryPrimitives.ReadUInt16LittleEndian(texel[(sample.BitOffset >> 3)..]);
                    }

                    break;
                case Ktx2NumericType.Ufloat:
                {
                    // An unsigned float of 5 exponent bits and 6 or 5 mantissa bits: the half with the same exponent and the mantissa in its high bits
                    var word = ReadWord(texel);
                    foreach (var sample in format.Samples)
                    {
                        var value = (word >> sample.BitOffset) & ((1u << sample.BitLength) - 1);
                        var mantissaBits = sample.BitLength - 5;
                        pixel[GetComponent(sample.Channel)] = (ushort)(value << (10 - mantissaBits));
                    }

                    break;
                }

                default:
                {
                    // E5B9G9R9: three 9-bit mantissas, then the exponent (bias 15) in the high bits
                    var word = ReadWord(texel);
                    var exponent = (int)(word >> 27);
                    for (var c = 0; c < 3; c++)
                    {
                        var mantissa = (word >> (9 * c)) & 0x1FF;
                        pixel[c] = BitConverter.HalfToUInt16Bits((Half)MathF.ScaleB(mantissa, exponent - 24));
                    }

                    break;
                }
            }
        }
    }

    private static void DecodeFloats(Ktx2VkFormat format, ReadOnlySpan<byte> source, int width, Span<float> destination)
    {
        var length = format.BytesPerBlock;
        for (var x = 0; x < width; x++)
        {
            var texel = source.Slice(x * length, length);
            var pixel = destination.Slice(x * 4, 4);
            pixel[0] = 0;
            pixel[1] = 0;
            pixel[2] = 0;
            pixel[3] = 1;
            foreach (var sample in format.Samples)
            {
                pixel[GetComponent(sample.Channel)] = BinaryPrimitives.ReadSingleLittleEndian(texel[(sample.BitOffset >> 3)..]);
            }
        }
    }

    private static uint ReadWord(ReadOnlySpan<byte> texel) => texel.Length == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(texel) : BinaryPrimitives.ReadUInt32LittleEndian(texel);
}
