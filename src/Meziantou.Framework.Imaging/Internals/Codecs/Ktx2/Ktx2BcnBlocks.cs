using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the texels of the BCn blocks of a KTX 2.0 texture (S3TC, RGTC and BPTC: <c>VK_FORMAT_BC1</c> to <c>BC7</c>) to
/// the RGBA rows of the decoder, with the block decoders DDS uses (<see cref="BlockDecoder"/>).
/// </summary>
/// <remarks>
/// The channels a format lacks are 0 for the colors and 1 for alpha (KTX section 5.4), as for the other formats: BC4 is
/// red, BC5 red and green, and BC1 without alpha (<c>BC1_RGB</c>) is opaque, the transparent texel of the three-color mode
/// being black. The unsigned formats give 8-bit texels, the signed RGTC formats single-precision floats in [-1, 1], and
/// BC6H halves.
/// </remarks>
internal static class Ktx2BcnBlocks
{
    /// <summary>Decodes one block to the 4x4 texels at <paramref name="destination"/>, rows <paramref name="stride"/> bytes apart.</summary>
    public static void Decode(Ktx2VkFormat format, ReadOnlySpan<byte> block, Span<byte> destination, int stride)
    {
        switch (format.Value)
        {
            case 131 or 132:
                DecodeBytes(block, destination, stride, BlockDecoder.DecodeBc1, opaque: true);
                break;
            case 133 or 134:
                DecodeBytes(block, destination, stride, BlockDecoder.DecodeBc1, opaque: false);
                break;
            case 135 or 136:
                DecodeBytes(block, destination, stride, BlockDecoder.DecodeBc2, opaque: false);
                break;
            case 137 or 138:
                DecodeBytes(block, destination, stride, BlockDecoder.DecodeBc3, opaque: false);
                break;
            case 145 or 146:
                DecodeBytes(block, destination, stride, BlockDecoder.DecodeBc7, opaque: false);
                break;
            case 139 or 141:
                DecodeUnsignedChannels(block, format.Value == 141 ? 2 : 1, destination, stride);
                break;
            case 140 or 142:
                DecodeSignedChannels(block, format.Value == 142 ? 2 : 1, destination, stride);
                break;
            case 143 or 144:
                DecodeHalves(block, format.Value == 144, destination, stride);
                break;
            default:
                throw new InvalidOperationException("The format is not a BCn format.");
        }
    }

    private static void DecodeBytes(ReadOnlySpan<byte> block, Span<byte> destination, int stride, BlockDecoderMethod decode, bool opaque)
    {
        Span<byte> texels = stackalloc byte[64];
        decode(block, texels);
        for (var y = 0; y < 4; y++)
        {
            var row = destination.Slice(y * stride, 16);
            texels.Slice(y * 16, 16).CopyTo(row);
            if (opaque)
            {
                for (var x = 0; x < 4; x++)
                {
                    row[(x * 4) + 3] = byte.MaxValue;
                }
            }
        }
    }

    private static void DecodeUnsignedChannels(ReadOnlySpan<byte> block, int channels, Span<byte> destination, int stride)
    {
        // Red, then green: 0 for the colors the format lacks, 255 for alpha
        Span<byte> texels = stackalloc byte[64];
        for (var i = 0; i < 16; i++)
        {
            texels[(i * 4) + 3] = byte.MaxValue;
        }

        for (var channel = 0; channel < channels; channel++)
        {
            BlockDecoder.DecodeUnsignedChannel(block.Slice(channel * BlockDecoder.SmallBlockLength, BlockDecoder.SmallBlockLength), texels[channel..], stride: 4);
        }

        for (var y = 0; y < 4; y++)
        {
            texels.Slice(y * 16, 16).CopyTo(destination.Slice(y * stride, 16));
        }
    }

    private static void DecodeSignedChannels(ReadOnlySpan<byte> block, int channels, Span<byte> destination, int stride)
    {
        Span<float> texels = stackalloc float[64];
        for (var i = 0; i < 16; i++)
        {
            texels[(i * 4) + 3] = 1f;
        }

        for (var channel = 0; channel < channels; channel++)
        {
            BlockDecoder.DecodeSignedChannel(block.Slice(channel * BlockDecoder.SmallBlockLength, BlockDecoder.SmallBlockLength), texels[channel..], stride: 4);
        }

        for (var y = 0; y < 4; y++)
        {
            var row = destination.Slice(y * stride, 64);
            for (var i = 0; i < 16; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(row[(i * 4)..], texels[(y * 16) + i]);
            }
        }
    }

    private static void DecodeHalves(ReadOnlySpan<byte> block, bool isSigned, Span<byte> destination, int stride)
    {
        Span<ushort> texels = stackalloc ushort[64];
        BlockDecoder.DecodeBc6H(block, texels, isSigned);
        for (var y = 0; y < 4; y++)
        {
            var row = destination.Slice(y * stride, 32);
            for (var i = 0; i < 16; i++)
            {
                // BC6H has no alpha: the block decoder writes 1
                BinaryPrimitives.WriteUInt16LittleEndian(row[(i * 2)..], texels[(y * 16) + i]);
            }
        }
    }

    private delegate void BlockDecoderMethod(ReadOnlySpan<byte> block, Span<byte> rgba);
}
