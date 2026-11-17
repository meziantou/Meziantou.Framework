using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the stored rows of a DDS surface (rows of pixels, or rows of 4×4 blocks) into rows of
/// <see cref="DdsPixelLayout.SourcePixelFormat"/>.
/// </summary>
internal sealed class DdsRowDecoder
{
    private readonly DdsPixelLayout _layout;
    private readonly MaskChannel _red;
    private readonly MaskChannel _green;
    private readonly MaskChannel _blue;
    private readonly MaskChannel _alpha;

    public DdsRowDecoder(DdsPixelLayout layout, int width)
    {
        _layout = layout;
        Width = width;
        SourceBytesPerPixel = layout.SourcePixelFormat switch
        {
            PixelFormat.Gray8 => 1,
            PixelFormat.Gray16 => 2,
            PixelFormat.Rgb24 => 3,
            PixelFormat.Rgba32 => 4,
            PixelFormat.Rgba64 or PixelFormat.RgbaHalf => 8,
            PixelFormat.RgbaFloat => 16,
            var format => throw new InvalidOperationException($"The DDS source layout {format} is not expected."),
        };

        if (layout.Encoding == DdsEncoding.Masks)
        {
            var wide = layout.IsWide;
            _red = new MaskChannel(layout.RedMask, wide);
            _green = new MaskChannel(layout.GreenMask, wide);
            _blue = new MaskChannel(layout.BlueMask, wide);
            _alpha = new MaskChannel(layout.AlphaMask, wide);
        }
    }

    /// <summary>Gets the width of the surface, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the number of pixel rows one stored row gives: 4 for a block-compressed format, otherwise 1.</summary>
    public int RowsPerStoredRow => _layout.IsBlockCompressed ? 4 : 1;

    /// <summary>Gets the number of bytes of a pixel of the source layout.</summary>
    public int SourceBytesPerPixel { get; }

    /// <summary>Gets the number of bytes of a row of the source layout.</summary>
    public int SourceRowLength => Width * SourceBytesPerPixel;

    /// <summary>Decodes one stored row into <see cref="RowsPerStoredRow"/> rows of the source layout.</summary>
    /// <param name="stored">The bytes of the stored row.</param>
    /// <param name="rows">The rows, <see cref="SourceRowLength"/> bytes each, one after the other.</param>
    public void Decode(ReadOnlySpan<byte> stored, Span<byte> rows)
    {
        if (_layout.IsBlockCompressed)
        {
            DecodeBlocks(stored, rows);
            return;
        }

        var bytesPerPixel = _layout.BytesPerPixel;
        for (var x = 0; x < Width; x++)
        {
            var pixel = stored.Slice(x * bytesPerPixel, bytesPerPixel);
            var destination = rows.Slice(x * SourceBytesPerPixel, SourceBytesPerPixel);
            switch (_layout.Encoding)
            {
                case DdsEncoding.Masks:
                    DecodeMasked(pixel, destination);
                    break;
                case DdsEncoding.Rgba16:
                    pixel.CopyTo(destination);
                    break;
                case DdsEncoding.Half:
                    DecodeHalf(pixel, destination);
                    break;
                case DdsEncoding.Float:
                    DecodeFloat(pixel, destination);
                    break;
                case DdsEncoding.R11G11B10Float:
                    DecodeR11G11B10(BinaryPrimitives.ReadUInt32LittleEndian(pixel), destination);
                    break;
                case DdsEncoding.R9G9B9E5:
                    DecodeR9G9B9E5(BinaryPrimitives.ReadUInt32LittleEndian(pixel), destination);
                    break;
                default:
                    throw new InvalidOperationException($"The DDS encoding {_layout.Encoding} is not an uncompressed format.");
            }
        }
    }

    private void DecodeBlocks(ReadOnlySpan<byte> stored, Span<byte> rows)
    {
        var blockLength = _layout.BlockLength;
        var rowLength = SourceRowLength;
        Span<byte> texels = stackalloc byte[64];
        Span<ushort> halves = stackalloc ushort[64];
        Span<float> floats = stackalloc float[32];
        for (var blockX = 0; blockX * 4 < Width; blockX++)
        {
            var block = stored.Slice(blockX * blockLength, blockLength);
            var columns = Math.Min(4, Width - (blockX * 4));
            switch (_layout.Encoding)
            {
                case DdsEncoding.Bc1:
                    BlockDecoder.DecodeBc1(block, texels);
                    CopyTexels(texels, 4, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc2:
                    BlockDecoder.DecodeBc2(block, texels);
                    CopyTexels(texels, 4, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc3:
                    BlockDecoder.DecodeBc3(block, texels);
                    CopyTexels(texels, 4, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc7:
                    BlockDecoder.DecodeBc7(block, texels);
                    CopyTexels(texels, 4, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc4:
                    BlockDecoder.DecodeUnsignedChannel(block, texels, stride: 1);
                    CopyTexels(texels, 1, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc5:
                    // Red and green; blue is 0
                    texels.Clear();
                    BlockDecoder.DecodeUnsignedChannel(block, texels, stride: 3);
                    BlockDecoder.DecodeUnsignedChannel(block[8..], texels[1..], stride: 3);
                    CopyTexels(texels[..48], 3, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc4Signed:
                    BlockDecoder.DecodeSignedChannel(block, floats, stride: 1);
                    WriteSignedTexels(floats, channels: 1, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc5Signed:
                    BlockDecoder.DecodeSignedChannel(block, floats, stride: 2);
                    BlockDecoder.DecodeSignedChannel(block[8..], floats[1..], stride: 2);
                    WriteSignedTexels(floats, channels: 2, rows, rowLength, blockX, columns);
                    break;
                case DdsEncoding.Bc6H or DdsEncoding.Bc6HSigned:
                    BlockDecoder.DecodeBc6H(block, halves, _layout.Encoding == DdsEncoding.Bc6HSigned);
                    for (var y = 0; y < 4; y++)
                    {
                        var row = rows.Slice(y * rowLength, rowLength);
                        for (var x = 0; x < columns; x++)
                        {
                            var destination = row.Slice(((blockX * 4) + x) * 8, 8);
                            for (var c = 0; c < 4; c++)
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(destination[(c * 2)..], halves[(((y * 4) + x) * 4) + c]);
                            }
                        }
                    }

                    break;
                default:
                    throw new InvalidOperationException($"The DDS encoding {_layout.Encoding} is not block-compressed.");
            }
        }
    }

    /// <summary>Copies the texels of a block that are inside the surface to the four rows.</summary>
    private static void CopyTexels(ReadOnlySpan<byte> texels, int bytesPerTexel, Span<byte> rows, int rowLength, int blockX, int columns)
    {
        for (var y = 0; y < 4; y++)
        {
            texels.Slice(y * 4 * bytesPerTexel, columns * bytesPerTexel).CopyTo(rows[((y * rowLength) + (blockX * 4 * bytesPerTexel))..]);
        }
    }

    /// <summary>Writes the floats of a signed RGTC block as RGBA floats: one channel as gray, two as red and green with a blue of 0.</summary>
    private static void WriteSignedTexels(ReadOnlySpan<float> samples, int channels, Span<byte> rows, int rowLength, int blockX, int columns)
    {
        for (var y = 0; y < 4; y++)
        {
            var row = rows.Slice(y * rowLength, rowLength);
            for (var x = 0; x < columns; x++)
            {
                var texel = (y * 4) + x;
                var destination = row.Slice(((blockX * 4) + x) * 16, 16);
                var red = samples[texel * channels];
                var green = channels == 1 ? red : samples[(texel * channels) + 1];
                var blue = channels == 1 ? red : 0f;
                WriteFloats(destination, red, green, blue, 1f);
            }
        }
    }

    private void DecodeMasked(ReadOnlySpan<byte> pixel, Span<byte> destination)
    {
        uint value = pixel.Length switch
        {
            1 => pixel[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(pixel),
            3 => pixel[0] | ((uint)pixel[1] << 8) | ((uint)pixel[2] << 16),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(pixel),
        };

        if (_layout.IsWide)
        {
            var max = (uint)ushort.MaxValue;
            var red = _red.Read(value, 0);
            var alpha = _alpha.Read(value, max);
            switch (_layout.SourcePixelFormat)
            {
                case PixelFormat.Gray16:
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)red);
                    return;
                case PixelFormat.Rgba64:
                    var green = _layout.IsGray ? red : _green.Read(value, 0);
                    var blue = _layout.IsGray ? red : _blue.Read(value, 0);
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)red);
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)green);
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)blue);
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)alpha);
                    return;
                default:
                    throw new InvalidOperationException($"The DDS source layout {_layout.SourcePixelFormat} is not a 16-bit layout.");
            }
        }

        var r = (byte)_red.Read(value, 0);
        var a = (byte)_alpha.Read(value, 255);
        switch (_layout.SourcePixelFormat)
        {
            case PixelFormat.Gray8:
                destination[0] = r;
                return;
            case PixelFormat.Rgb24:
                destination[0] = r;
                destination[1] = (byte)_green.Read(value, 0);
                destination[2] = (byte)_blue.Read(value, 0);
                return;
            case PixelFormat.Rgba32:
                // Gray with alpha repeats the gray level; an alpha mask is black
                destination[0] = r;
                destination[1] = _layout.IsGray ? r : (byte)_green.Read(value, 0);
                destination[2] = _layout.IsGray ? r : (byte)_blue.Read(value, 0);
                destination[3] = a;
                return;
            default:
                throw new InvalidOperationException($"The DDS source layout {_layout.SourcePixelFormat} is not an 8-bit layout.");
        }
    }

    private void DecodeHalf(ReadOnlySpan<byte> pixel, Span<byte> destination)
    {
        const ushort One = 0x3C00;
        var red = BinaryPrimitives.ReadUInt16LittleEndian(pixel);
        switch (_layout.Channels)
        {
            case 1:
                WriteHalves(destination, red, red, red, One);
                break;
            case 2:
                WriteHalves(destination, red, BinaryPrimitives.ReadUInt16LittleEndian(pixel[2..]), 0, One);
                break;
            default:
                pixel[..8].CopyTo(destination);
                break;
        }
    }

    private void DecodeFloat(ReadOnlySpan<byte> pixel, Span<byte> destination)
    {
        var red = BinaryPrimitives.ReadSingleLittleEndian(pixel);
        switch (_layout.Channels)
        {
            case 1:
                WriteFloats(destination, red, red, red, 1f);
                break;
            case 2:
                WriteFloats(destination, red, BinaryPrimitives.ReadSingleLittleEndian(pixel[4..]), 0f, 1f);
                break;
            case 3:
                WriteFloats(destination, red, BinaryPrimitives.ReadSingleLittleEndian(pixel[4..]), BinaryPrimitives.ReadSingleLittleEndian(pixel[8..]), 1f);
                break;
            default:
                pixel[..16].CopyTo(destination);
                break;
        }
    }

    /// <summary>
    /// R11G11B10_FLOAT: each channel is an unsigned float with the 5-bit exponent of a half and a mantissa of 6 bits (red,
    /// green) or 5 bits (blue), so it is a half whose low mantissa bits are 0, infinities and NaNs included.
    /// </summary>
    private static void DecodeR11G11B10(uint value, Span<byte> destination)
    {
        var red = (ushort)((value & 0x7FF) << 4);
        var green = (ushort)(((value >> 11) & 0x7FF) << 4);
        var blue = (ushort)(((value >> 22) & 0x3FF) << 5);
        WriteHalves(destination, red, green, blue, 0x3C00);
    }

    /// <summary>
    /// R9G9B9E5_SHAREDEXP: each channel is <c>mantissa × 2^(exponent − 24)</c>, a mantissa of 9 bits; every such value is a
    /// half (at most 65,408, a multiple of 2^-24), so the conversion is exact.
    /// </summary>
    private static void DecodeR9G9B9E5(uint value, Span<byte> destination)
    {
        var exponent = (int)(value >> 27);
        var scale = Math.ScaleB(1.0, exponent - 24);
        var red = BitConverter.HalfToUInt16Bits((Half)((value & 0x1FF) * scale));
        var green = BitConverter.HalfToUInt16Bits((Half)(((value >> 9) & 0x1FF) * scale));
        var blue = BitConverter.HalfToUInt16Bits((Half)(((value >> 18) & 0x1FF) * scale));
        WriteHalves(destination, red, green, blue, 0x3C00);
    }

    private static void WriteHalves(Span<byte> destination, ushort red, ushort green, ushort blue, ushort alpha)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, red);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], green);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], blue);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], alpha);
    }

    private static void WriteFloats(Span<byte> destination, float red, float green, float blue, float alpha)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, red);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], green);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], blue);
        BinaryPrimitives.WriteSingleLittleEndian(destination[12..], alpha);
    }

    /// <summary>A channel of a masked pixel, expanded to 8 or 16 bits.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct MaskChannel
    {
        private readonly uint _mask;
        private readonly int _shift;
        private readonly ulong _max;
        private readonly ulong _target;

        public MaskChannel(uint mask, bool wide)
        {
            _mask = mask;
            _shift = mask == 0 ? 0 : BitOperations.TrailingZeroCount(mask);
            var bits = BitOperations.PopCount(mask);
            _max = bits == 0 ? 1 : (1UL << bits) - 1;
            _target = wide ? ushort.MaxValue : byte.MaxValue;
        }

        /// <summary>Reads the channel: <c>round(value × target / max)</c>, or <paramref name="absent"/> when the channel is not stored.</summary>
        public uint Read(uint pixel, uint absent)
        {
            if (_mask == 0)
                return absent;

            var value = (ulong)((pixel & _mask) >> _shift);
            return (uint)(((value * _target) + (_max / 2)) / _max);
        }
    }
}
