using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The block encoders of BC1 to BC7, ETC2, EAC and ASTC, and the DDS and KTX 2.0 encoders. A block of any texels must
/// encode without an exception, to the same bytes every time, to a block the decoder of the library reads, with the
/// properties the formats guarantee: the 1-bit alpha of BC1 and of ETC2 punchthrough exact, opaque blocks opaque, solid
/// blocks of BC7 and ASTC exact, BC6H finite. A small image saved by the texture encoders with any settings is either
/// rejected with a documented exception or read back with its size, every mip level, and, in the default lossless
/// format, its exact pixels.
/// </summary>
public static partial class ImagingFuzzTargets
{
    private static readonly (int Width, int Height)[] AstcFootprints = [(4, 4), (5, 4), (5, 5), (6, 5), (6, 6), (8, 5), (8, 6), (8, 8), (10, 5), (10, 6), (10, 8), (10, 10), (12, 10), (12, 12)];

    private enum BlockKind
    {
        Bc1,
        Bc1Opaque,
        Bc2,
        Bc3,
        Bc4,
        Bc4Signed,
        Bc6H,
        Bc6HSigned,
        Bc7,
        Etc2Rgb,
        Etc2Punchthrough,
        Etc2Rgba,
        EacUnsigned,
        EacSigned,
        Astc,
    }

    private static void TextureBlockTarget(ReadOnlySpan<byte> data)
    {
        // A byte of kind and quality, a byte of ASTC footprint and profile, then the texels
        var selector = FuzzInput.ReadByte(ref data);
        var kind = (BlockKind)((selector & 0x7F) % 15);
        var thorough = (selector & 0x80) != 0;
        var astc = FuzzInput.ReadByte(ref data);
        var (blockWidth, blockHeight) = kind == BlockKind.Astc ? AstcFootprints[(astc & 0x7F) % AstcFootprints.Length] : (4, 4);
        var srgb = (astc & 0x80) != 0;
        var texels = new byte[blockWidth * blockHeight * 4];
        for (var i = 0; i < texels.Length && !data.IsEmpty; i++)
        {
            texels[i] = data[i % data.Length];
        }

        var block = Encode(kind, texels, blockWidth, blockHeight, srgb, thorough);
        FuzzAssert.True(block.AsSpan().SequenceEqual(Encode(kind, texels, blockWidth, blockHeight, srgb, thorough)), "The same texels give other bytes");

        var solid = true;
        var opaque = true;
        for (var i = 0; i < texels.Length; i += 4)
        {
            solid &= texels.AsSpan(i, 4).SequenceEqual(texels.AsSpan(0, 4));
            opaque &= texels[i + 3] == 255;
        }

        var decoded = new byte[blockWidth * blockHeight * 4];
        switch (kind)
        {
            case BlockKind.Bc1 or BlockKind.Bc1Opaque:
                BlockDecoder.DecodeBc1(block, decoded);
                for (var i = 0; i < 16; i++)
                {
                    var transparent = kind == BlockKind.Bc1 && texels[(i * 4) + 3] < 128;
                    FuzzAssert.Equal(transparent ? (byte)0 : (byte)255, decoded[(i * 4) + 3], "The 1-bit alpha of BC1");
                }

                break;

            case BlockKind.Bc2 or BlockKind.Bc3 or BlockKind.Bc7:
                if (kind == BlockKind.Bc2)
                {
                    BlockDecoder.DecodeBc2(block, decoded);
                }
                else if (kind == BlockKind.Bc3)
                {
                    BlockDecoder.DecodeBc3(block, decoded);
                }
                else
                {
                    BlockDecoder.DecodeBc7(block, decoded);
                    FuzzAssert.True(!solid || decoded.AsSpan().SequenceEqual(texels), "A solid BC7 block is not exact");
                }

                for (var i = 0; i < 16 && opaque; i++)
                {
                    FuzzAssert.Equal((byte)255, decoded[(i * 4) + 3], "The alpha of an opaque block");
                }

                break;

            case BlockKind.Bc4:
                BlockDecoder.DecodeUnsignedChannel(block, decoded, 4);
                break;

            case BlockKind.Bc4Signed:
            {
                var values = new float[16];
                BlockDecoder.DecodeSignedChannel(block, values, 1);
                foreach (var value in values)
                {
                    FuzzAssert.True(value is >= -1f and <= 1f, "A signed BC4 value outside [-1, 1]");
                }

                break;
            }

            case BlockKind.Bc6H or BlockKind.Bc6HSigned:
            {
                var halves = new ushort[64];
                BlockDecoder.DecodeBc6H(block, halves, kind == BlockKind.Bc6HSigned);
                foreach (var half in halves)
                {
                    FuzzAssert.True(Half.IsFinite(BitConverter.UInt16BitsToHalf(half)), "A BC6H value that is not finite");
                }

                break;
            }

            case BlockKind.Etc2Rgb or BlockKind.Etc2Punchthrough or BlockKind.Etc2Rgba:
            {
                var value = kind switch { BlockKind.Etc2Rgb => 147, BlockKind.Etc2Punchthrough => 149, _ => 151 };
                _ = Ktx2VkFormats.TryGet(value, out var format);
                Etc2BlockDecoder.Decode(format!, block, decoded, 16);
                for (var i = 0; i < 16; i++)
                {
                    var expected = kind switch
                    {
                        BlockKind.Etc2Rgb => 255,
                        BlockKind.Etc2Punchthrough => texels[(i * 4) + 3] < 128 ? 0 : 255,
                        _ => opaque ? 255 : -1,
                    };
                    FuzzAssert.True(expected < 0 || decoded[(i * 4) + 3] == expected, "The alpha of an ETC2 block");
                }

                break;
            }

            case BlockKind.EacUnsigned or BlockKind.EacSigned:
            {
                var signed = kind == BlockKind.EacSigned;
                _ = Ktx2VkFormats.TryGet(signed ? 154 : 153, out var format);
                Etc2BlockDecoder.DecodeEac(format!, block, new byte[16 * 16], 64);
                break;
            }

            default:
                // A legal LDR block: the decoder rejects illegal and HDR blocks
                AstcBlockDecoder.Decode(block, blockWidth, blockHeight, srgb ? AstcProfile.Srgb : AstcProfile.LinearUnorm8, decoded, blockWidth * 4);
                FuzzAssert.True(!solid || !srgb || decoded.AsSpan().SequenceEqual(texels), "A solid ASTC block is not exact");
                break;
        }
    }

    private static byte[] Encode(BlockKind kind, byte[] texels, int blockWidth, int blockHeight, bool srgb, bool thorough)
    {
        var block = new byte[kind is BlockKind.Bc1 or BlockKind.Bc1Opaque or BlockKind.Bc4 or BlockKind.Bc4Signed or BlockKind.Etc2Rgb or BlockKind.Etc2Punchthrough or BlockKind.EacUnsigned or BlockKind.EacSigned ? 8 : 16];
        switch (kind)
        {
            case BlockKind.Bc1:
                S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: true, thorough);
                break;
            case BlockKind.Bc1Opaque:
                S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: false, thorough);
                break;
            case BlockKind.Bc2:
                S3tcBlockEncoder.EncodeBc2(texels, block, thorough);
                break;
            case BlockKind.Bc3:
                S3tcBlockEncoder.EncodeBc3(texels, block, thorough);
                break;
            case BlockKind.Bc4:
                S3tcBlockEncoder.EncodeUnsignedChannel(texels, 4, block, thorough);
                break;
            case BlockKind.Bc4Signed:
                S3tcBlockEncoder.EncodeSignedChannel(GetFloats(texels), 1, block, thorough);
                break;
            case BlockKind.Bc6H or BlockKind.Bc6HSigned:
            {
                // Any bit patterns: NaN, infinities and negative values included
                var halves = new ushort[64];
                for (var i = 0; i < 32; i++)
                {
                    halves[i] = (ushort)(texels[2 * i] | (texels[(2 * i) + 1] << 8));
                    halves[i + 32] = (ushort)(texels[(2 * i) + 1] | (texels[2 * i] << 8));
                }

                Bc6HBlockEncoder.Encode(halves, block, kind == BlockKind.Bc6HSigned, thorough);
                break;
            }

            case BlockKind.Bc7:
                Bc7BlockEncoder.Encode(texels, block, thorough);
                break;
            case BlockKind.Etc2Rgb:
                Etc2BlockEncoder.EncodeRgb(texels, block, thorough);
                break;
            case BlockKind.Etc2Punchthrough:
                Etc2BlockEncoder.EncodePunchthrough(texels, block, thorough);
                break;
            case BlockKind.Etc2Rgba:
                Etc2BlockEncoder.EncodeRgba(texels, block, thorough);
                break;
            case BlockKind.EacUnsigned:
            {
                var words = new ushort[16];
                for (var i = 0; i < 16; i++)
                {
                    words[i] = (ushort)(texels[i * 4] | (texels[(i * 4) + 1] << 8));
                }

                Etc2BlockEncoder.EncodeEacUnsigned(words, 1, block, thorough);
                break;
            }

            case BlockKind.EacSigned:
                Etc2BlockEncoder.EncodeEacSigned(GetFloats(texels), 1, block, thorough);
                break;
            default:
                AstcBlockEncoder.Encode(texels, blockWidth, blockHeight, srgb, block, thorough);
                break;
        }

        return block;
    }

    /// <summary>Reads 16 floats of any bit pattern: NaN, infinities and values outside [-1, 1] included.</summary>
    private static float[] GetFloats(byte[] texels)
    {
        var values = new float[16];
        for (var i = 0; i < 16; i++)
        {
            values[i] = BitConverter.ToSingle(texels, i * 4);
        }

        return values;
    }

    private static void TextureEncoderTarget(ReadOnlySpan<byte> data)
    {
        // A byte of settings, a byte of texture format, then an image
        var settings = FuzzInput.ReadByte(ref data);
        var formatByte = FuzzInput.ReadByte(ref data);
        var dds = (settings & 1) != 0;
        var mipmaps = (settings & 2) != 0;
        var quality = (settings & 4) != 0 ? TextureCompressionQuality.Thorough : TextureCompressionQuality.Fast;
        var zlib = (settings & 8) != 0;
        var lossless = (formatByte & 0x80) != 0;
        using var source = CreateImage(ref data, maxSide: 12);
        using var image = source.Frames.Count == 1 && source.PosterFrame is null ? source.Clone() : source.CloneFrame(0);
        ImageEncoder encoder = dds
            ? new DdsEncoder
            {
                TextureFormat = lossless ? null : (DdsTextureFormat)((formatByte & 0x7F) % 24),
                GenerateMipmaps = mipmaps,
                Quality = quality,
                AllowBitDepthReduction = true,
                AllowChannelReduction = true,
                MetadataHandling = MetadataHandling.DiscardUnsupported,
                FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip },
            }
            : new Ktx2Encoder
            {
                TextureFormat = lossless ? null : (Ktx2TextureFormat)((formatByte & 0x7F) % 39),
                GenerateMipmaps = mipmaps,
                Quality = quality,
                Supercompression = zlib ? Ktx2Supercompression.Zlib : Ktx2Supercompression.None,
                AllowBitDepthReduction = true,
                AllowChannelReduction = true,
                MetadataHandling = MetadataHandling.DiscardUnsupported,
                FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip },
            };

        byte[] file;
        try
        {
            using var output = new MemoryStream();
            image.Save(output, encoder);
            file = output.ToArray();
        }
        catch (UnsupportedImageFeatureException)
        {
            // Samples the format cannot store (outside its range, not numbers), which the encoders never clamp
            return;
        }

        using var collection = ImageCollection.Load(file);
        var levels = mipmaps ? TextureMipmaps.GetLevelCount(image.Width, image.Height) : 1;
        FuzzAssert.Equal(levels, collection.Count, "The number of mip levels");
        for (var level = 0; level < collection.Count; level++)
        {
            FuzzAssert.Equal(new Size(Math.Max(1, image.Width >> level), Math.Max(1, image.Height >> level)), collection[level].Size, "The size of a mip level");
            using var decoded = collection[level].Decode();
            if (level == 0 && lossless)
            {
                // The default format stores the pixels without loss
                using var expected = image.CloneAs(decoded.PixelFormat, options: null);
                FuzzAssert.True(GetPixelBytes(expected).AsSpan().SequenceEqual(GetPixelBytes(decoded)), "The default format changes the pixels");
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetTextureBlockSeeds()
    {
        // Every kind at both levels, on the patterns of the unit tests: a gradient, noise with alpha, a solid color
        byte[] gradient = [.. Enumerable.Range(0, 64).Select(i => (byte)(i % 4 == 3 ? 255 : i * 4))];
        byte[] noise = [.. Enumerable.Range(0, 64).Select(i => (byte)((i * 2654435761u) >> 24))];
        byte[] solid = [.. Enumerable.Range(0, 64).Select(i => (byte)(i % 4 == 0 ? 200 : i % 4 == 3 ? 255 : 40))];
        for (var kind = 0; kind < 15; kind++)
        {
            yield return new FuzzSeed($"block-{kind}-gradient", (byte[])[(byte)kind, (byte)kind, .. gradient]);
            yield return new FuzzSeed($"block-{kind}-noise-thorough", (byte[])[(byte)(kind | 0x80), (byte)(kind | 0x80), .. noise]);
            yield return new FuzzSeed($"block-{kind}-solid", (byte[])[(byte)kind, (byte)(kind + 7), .. solid]);
        }
    }

    private static IEnumerable<FuzzSeed> GetTextureEncoderSeeds()
    {
        // Each container, with and without mipmaps, in the lossless default format and in compressed formats, from images of
        // several pixel formats
        yield return new FuzzSeed("dds-default-rgba32", (byte[])[1, 0x80, 0, 5, 3, 0, 10, 200, 30, 255, 99]);
        yield return new FuzzSeed("dds-bc7-mipmaps", (byte[])[3 | 4, 22, 0, 9, 6, 0, 1, 2, 3, 250, 128, 64]);
        yield return new FuzzSeed("dds-bc6h-float", (byte[])[1, 20, 6, 4, 4, 0, 0, 0, 128, 63, 0, 0, 0, 64]);
        yield return new FuzzSeed("dds-bc1-gray", (byte[])[3, 10, 2, 7, 7, 0, 17, 230]);
        yield return new FuzzSeed("ktx2-default-gray16", (byte[])[2, 0x80, 3, 3, 3, 0, 255, 0, 17, 0]);
        yield return new FuzzSeed("ktx2-astc-zlib", (byte[])[2 | 8, 29, 0, 11, 7, 0, 200, 10, 90, 255]);
        yield return new FuzzSeed("ktx2-etc2-rgba", (byte[])[4, 20, 0, 8, 8, 0, 5, 6, 7, 8]);
        yield return new FuzzSeed("ktx2-eac-signed-half", (byte[])[0, 24, 7, 5, 2, 0, 0, 60, 0, 188]);
    }
}
