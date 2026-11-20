using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the working samples of a surface to the texels of a format, one row of blocks at a time: the uncompressed
/// formats are packed, the block-compressed ones go through the block encoders (<see cref="S3tcBlockEncoder"/>,
/// <see cref="Bc6HBlockEncoder"/>, <see cref="Bc7BlockEncoder"/>, <see cref="Etc2BlockEncoder"/>,
/// <see cref="AstcBlockEncoder"/>).
/// </summary>
/// <remarks>
/// A block that crosses the right or the bottom edge of the surface repeats the last column and the last row, so that
/// the texels outside the surface, which no reader shows, cost nothing to the texels inside it.
/// </remarks>
internal static class TextureBlockWriter
{
    /// <summary>Gets the length of a row of blocks of a surface, in bytes.</summary>
    public static int GetBlockRowLength(TextureTarget target, int width) => TextureTarget.GetBlockCount(width, target.BlockWidth) * target.BytesPerBlock;

    /// <summary>Encodes a row of blocks.</summary>
    /// <param name="pixels">The working samples of the surface, of the precision of <paramref name="target"/>.</param>
    /// <param name="target">The format.</param>
    /// <param name="thorough">Whether the block encoders search thoroughly.</param>
    /// <param name="blockRow">The index of the row of blocks.</param>
    /// <param name="destination">The bytes of the row (<see cref="GetBlockRowLength"/>).</param>
    public static void EncodeBlockRow(TexturePixels pixels, TextureTarget target, bool thorough, int blockRow, Span<byte> destination)
    {
        if (!target.IsBlockCompressed)
        {
            PackRow(pixels, target.Encoding, blockRow, destination);
            return;
        }

        var blockWidth = target.BlockWidth;
        var blockHeight = target.BlockHeight;
        var texels = blockWidth * blockHeight;
        Span<byte> bytes = stackalloc byte[texels * 4];
        Span<ushort> words = stackalloc ushort[16 * 4];
        Span<float> floats = stackalloc float[16 * 4];
        var columns = TextureTarget.GetBlockCount(pixels.Width, blockWidth);
        for (var column = 0; column < columns; column++)
        {
            var block = destination.Slice(column * target.BytesPerBlock, target.BytesPerBlock);
            var x0 = column * blockWidth;
            var y0 = blockRow * blockHeight;
            switch (pixels.Precision)
            {
                case TexturePrecision.Byte:
                    GatherBytes(pixels, x0, y0, blockWidth, blockHeight, bytes);
                    break;
                case TexturePrecision.UInt16:
                    GatherWords(pixels, x0, y0, words);
                    break;
                default:
                    GatherFloats(pixels, x0, y0, floats);
                    break;
            }

            switch (target.Encoding)
            {
                case TextureEncoding.Bc1:
                    S3tcBlockEncoder.EncodeBc1(bytes, block, allowTransparency: true, thorough);
                    break;
                case TextureEncoding.Bc1Opaque:
                    S3tcBlockEncoder.EncodeBc1(bytes, block, allowTransparency: false, thorough);
                    break;
                case TextureEncoding.Bc2:
                    S3tcBlockEncoder.EncodeBc2(bytes, block, thorough);
                    break;
                case TextureEncoding.Bc3:
                    S3tcBlockEncoder.EncodeBc3(bytes, block, thorough);
                    break;
                case TextureEncoding.Bc4:
                    S3tcBlockEncoder.EncodeUnsignedChannel(bytes, 4, block, thorough);
                    break;
                case TextureEncoding.Bc5:
                    S3tcBlockEncoder.EncodeUnsignedChannel(bytes, 4, block[..8], thorough);
                    S3tcBlockEncoder.EncodeUnsignedChannel(bytes[1..], 4, block[8..], thorough);
                    break;
                case TextureEncoding.Bc4Signed:
                    S3tcBlockEncoder.EncodeSignedChannel(floats, 4, block, thorough);
                    break;
                case TextureEncoding.Bc5Signed:
                    S3tcBlockEncoder.EncodeSignedChannel(floats, 4, block[..8], thorough);
                    S3tcBlockEncoder.EncodeSignedChannel(floats[1..], 4, block[8..], thorough);
                    break;
                case TextureEncoding.Bc6H or TextureEncoding.Bc6HSigned:
                    for (var i = 0; i < 64; i++)
                    {
                        words[i] = BitConverter.HalfToUInt16Bits((Half)floats[i]);
                    }

                    Bc6HBlockEncoder.Encode(words, block, target.Encoding == TextureEncoding.Bc6HSigned, thorough);
                    break;
                case TextureEncoding.Bc7:
                    Bc7BlockEncoder.Encode(bytes, block, thorough);
                    break;
                case TextureEncoding.Etc2Rgb:
                    Etc2BlockEncoder.EncodeRgb(bytes, block, thorough);
                    break;
                case TextureEncoding.Etc2RgbA1:
                    Etc2BlockEncoder.EncodePunchthrough(bytes, block, thorough);
                    break;
                case TextureEncoding.Etc2Rgba:
                    Etc2BlockEncoder.EncodeRgba(bytes, block, thorough);
                    break;
                case TextureEncoding.EacR11:
                    Etc2BlockEncoder.EncodeEacUnsigned(words, 4, block, thorough);
                    break;
                case TextureEncoding.EacRg11:
                    Etc2BlockEncoder.EncodeEacUnsigned(words, 4, block[..8], thorough);
                    Etc2BlockEncoder.EncodeEacUnsigned(words[1..], 4, block[8..], thorough);
                    break;
                case TextureEncoding.EacR11Signed:
                    Etc2BlockEncoder.EncodeEacSigned(floats, 4, block, thorough);
                    break;
                case TextureEncoding.EacRg11Signed:
                    Etc2BlockEncoder.EncodeEacSigned(floats, 4, block[..8], thorough);
                    Etc2BlockEncoder.EncodeEacSigned(floats[1..], 4, block[8..], thorough);
                    break;
                case TextureEncoding.Astc:
                    AstcBlockEncoder.Encode(bytes, blockWidth, blockHeight, target.IsSrgb, block, thorough);
                    break;
                default:
                    throw new InvalidOperationException("The encoding is not block-compressed.");
            }
        }
    }

    private static void GatherBytes(TexturePixels pixels, int x0, int y0, int blockWidth, int blockHeight, Span<byte> texels)
    {
        for (var y = 0; y < blockHeight; y++)
        {
            var row = pixels.GetBytes(Math.Min(y0 + y, pixels.Height - 1));
            for (var x = 0; x < blockWidth; x++)
            {
                var sx = Math.Min(x0 + x, pixels.Width - 1);
                row.Slice(sx * 4, 4).CopyTo(texels[(((y * blockWidth) + x) * 4)..]);
            }
        }
    }

    private static void GatherWords(TexturePixels pixels, int x0, int y0, Span<ushort> texels)
    {
        for (var y = 0; y < 4; y++)
        {
            var row = pixels.GetWords(Math.Min(y0 + y, pixels.Height - 1));
            for (var x = 0; x < 4; x++)
            {
                var sx = Math.Min(x0 + x, pixels.Width - 1);
                row.Slice(sx * 4, 4).CopyTo(texels[(((y * 4) + x) * 4)..]);
            }
        }
    }

    private static void GatherFloats(TexturePixels pixels, int x0, int y0, Span<float> texels)
    {
        for (var y = 0; y < 4; y++)
        {
            var row = pixels.GetFloats(Math.Min(y0 + y, pixels.Height - 1));
            for (var x = 0; x < 4; x++)
            {
                var sx = Math.Min(x0 + x, pixels.Width - 1);
                row.Slice(sx * 4, 4).CopyTo(texels[(((y * 4) + x) * 4)..]);
            }
        }
    }

    /// <summary>Packs a row of texels of an uncompressed format, little-endian.</summary>
    private static void PackRow(TexturePixels pixels, TextureEncoding encoding, int y, Span<byte> destination)
    {
        var width = pixels.Width;
        switch (encoding)
        {
            case TextureEncoding.Rgba8:
                pixels.GetBytes(y).CopyTo(destination);
                break;

            case TextureEncoding.Bgra8:
            {
                var row = pixels.GetBytes(y);
                for (var x = 0; x < width; x++)
                {
                    destination[(x * 4) + 0] = row[(x * 4) + 2];
                    destination[(x * 4) + 1] = row[(x * 4) + 1];
                    destination[(x * 4) + 2] = row[(x * 4) + 0];
                    destination[(x * 4) + 3] = row[(x * 4) + 3];
                }

                break;
            }

            case TextureEncoding.R8:
            {
                var row = pixels.GetBytes(y);
                for (var x = 0; x < width; x++)
                {
                    destination[x] = row[x * 4];
                }

                break;
            }

            case TextureEncoding.Rg8:
            {
                var row = pixels.GetBytes(y);
                for (var x = 0; x < width; x++)
                {
                    destination[x * 2] = row[x * 4];
                    destination[(x * 2) + 1] = row[(x * 4) + 1];
                }

                break;
            }

            case TextureEncoding.R16:
            {
                var row = pixels.GetWords(y);
                for (var x = 0; x < width; x++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[(x * 2)..], row[x * 4]);
                }

                break;
            }

            case TextureEncoding.Rgba16:
            {
                var row = pixels.GetWords(y);
                for (var i = 0; i < width * 4; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], row[i]);
                }

                break;
            }

            case TextureEncoding.RgbaHalf:
            {
                var row = pixels.GetFloats(y);
                for (var i = 0; i < width * 4; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], TexturePixels.ToHalfBits(row[i]));
                }

                break;
            }

            case TextureEncoding.RgbaFloat:
            {
                var row = pixels.GetFloats(y);
                for (var i = 0; i < width * 4; i++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(destination[(i * 4)..], row[i]);
                }

                break;
            }

            default:
                throw new InvalidOperationException("The encoding is block-compressed.");
        }
    }
}
