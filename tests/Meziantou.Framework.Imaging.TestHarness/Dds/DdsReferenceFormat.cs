using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.Dds;

/// <summary>A pixel format as the reference reader knows it.</summary>
public sealed class DdsReferenceFormat
{
    private readonly DecodePixel? _decode;

    private DdsReferenceFormat(string name, int bytesPerPixel, int blockLength, DecodePixel? decode)
    {
        Name = name;
        BytesPerPixel = bytesPerPixel;
        BlockLength = blockLength;
        _decode = decode;
    }

    private delegate void DecodePixel(ReadOnlySpan<byte> pixel, Span<double> rgba);

    public string Name { get; }

    /// <summary>Gets the number of bytes of a pixel, 0 for a block-compressed format.</summary>
    public int BytesPerPixel { get; }

    /// <summary>Gets the number of bytes of a 4×4 block, 0 for an uncompressed format.</summary>
    public int BlockLength { get; }

    public long GetSurfaceLength(int width, int height) => BlockLength > 0
        ? (long)((width + 3) / 4) * ((height + 3) / 4) * BlockLength
        : (long)width * height * BytesPerPixel;

    public void Decode(ReadOnlySpan<byte> pixel, Span<double> rgba) => (_decode ?? throw new InvalidOperationException(Name + " is block-compressed."))(pixel, rgba);

    public static DdsReferenceFormat FromDxgi(uint format) => format switch
    {
        2 => Floats("R32G32B32A32_FLOAT", 4),
        6 => Floats("R32G32B32_FLOAT", 3),
        10 => Halves("R16G16B16A16_FLOAT", 4),
        11 => Unorm16("R16G16B16A16_UNORM", 4),
        16 => Floats("R32G32_FLOAT", 2),
        24 => Masks("R10G10B10A2_UNORM", 4, 0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000, gray: false),
        26 => new("R11G11B10_FLOAT", 4, 0, (p, rgba) => SetRgba(rgba, SmallFloat(Read(p) & 0x7FF, 6), SmallFloat((Read(p) >> 11) & 0x7FF, 6), SmallFloat(Read(p) >> 22, 5), 1)),
        27 or 28 or 29 => Masks("R8G8B8A8_UNORM", 4, 0xFF, 0xFF00, 0xFF0000, 0xFF000000, gray: false),
        34 => Halves("R16G16_FLOAT", 2),
        35 => Masks("R16G16_UNORM", 4, 0xFFFF, 0xFFFF0000, 0, 0, gray: false),
        41 => Floats("R32_FLOAT", 1),
        49 => Masks("R8G8_UNORM", 2, 0xFF, 0xFF00, 0, 0, gray: false),
        54 => Halves("R16_FLOAT", 1),
        56 => Masks("R16_UNORM", 2, 0xFFFF, 0, 0, 0, gray: true),
        61 => Masks("R8_UNORM", 1, 0xFF, 0, 0, 0, gray: true),
        65 => Masks("A8_UNORM", 1, 0, 0, 0, 0xFF, gray: false),
        67 => new("R9G9B9E5_SHAREDEXP", 4, 0, (p, rgba) =>
        {
            var value = Read(p);
            var scale = Math.Pow(2, (int)(value >> 27) - 15 - 9);
            SetRgba(rgba, (value & 0x1FF) * scale, ((value >> 9) & 0x1FF) * scale, ((value >> 18) & 0x1FF) * scale, 1);
        }),
        >= 70 and <= 72 or >= 79 and <= 81 => new($"DXGI {format}", 0, 8, null),
        >= 73 and <= 78 or >= 82 and <= 84 or >= 94 and <= 99 => new($"DXGI {format}", 0, 16, null),
        85 => Masks("B5G6R5_UNORM", 2, 0xF800, 0x7E0, 0x1F, 0, gray: false),
        86 => Masks("B5G5R5A1_UNORM", 2, 0x7C00, 0x3E0, 0x1F, 0x8000, gray: false),
        87 or 90 or 91 => Masks("B8G8R8A8_UNORM", 4, 0xFF0000, 0xFF00, 0xFF, 0xFF000000, gray: false),
        88 or 92 or 93 => Masks("B8G8R8X8_UNORM", 4, 0xFF0000, 0xFF00, 0xFF, 0, gray: false),
        115 => Masks("B4G4R4A4_UNORM", 2, 0xF00, 0xF0, 0xF, 0xF000, gray: false),
        _ => new($"DXGI {format}", 1, 0, null),
    };

    public static DdsReferenceFormat FromLegacy(uint flags, uint fourCC, uint bitCount, uint red, uint green, uint blue, uint alpha)
    {
        if ((flags & DdsFileBuilder.PixelFourCC) != 0)
        {
            return fourCC switch
            {
                36 => Unorm16("A16B16G16R16", 4),
                111 => Halves("R16F", 1),
                112 => Halves("G16R16F", 2),
                113 => Halves("A16B16G16R16F", 4),
                114 => Floats("R32F", 1),
                115 => Floats("G32R32F", 2),
                116 => Floats("A32B32G32R32F", 4),
                _ when fourCC == DdsFileBuilder.FourCC("DXT1") || fourCC == DdsFileBuilder.FourCC("ATI1") || fourCC == DdsFileBuilder.FourCC("BC4U") || fourCC == DdsFileBuilder.FourCC("BC4S") => new("FourCC block 8", 0, 8, null),
                _ => new("FourCC block 16", 0, 16, null),
            };
        }

        var alphaMask = (flags & DdsFileBuilder.PixelAlphaPixels) != 0 || (flags & DdsFileBuilder.PixelAlpha) != 0 ? alpha : 0;
        if ((flags & DdsFileBuilder.PixelLuminance) != 0)
            return Masks("luminance", (int)bitCount / 8, red, 0, 0, alphaMask, gray: true);

        if ((flags & DdsFileBuilder.PixelAlpha) != 0)
            return Masks("alpha", (int)bitCount / 8, 0, 0, 0, alphaMask, gray: false);

        return Masks("rgb", (int)bitCount / 8, red, green, blue, alphaMask, gray: false);
    }

    private static uint Read(ReadOnlySpan<byte> pixel) => pixel.Length switch
    {
        1 => pixel[0],
        2 => BinaryPrimitives.ReadUInt16LittleEndian(pixel),
        3 => (uint)(pixel[0] | (pixel[1] << 8) | (pixel[2] << 16)),
        _ => BinaryPrimitives.ReadUInt32LittleEndian(pixel),
    };

    /// <summary>A field of a mask, as the fraction of its maximum.</summary>
    private static double Field(uint pixel, uint mask)
    {
        var max = mask >> BitOperations.TrailingZeroCount(mask);
        return (double)((pixel & mask) >> BitOperations.TrailingZeroCount(mask)) / max;
    }

    private static DdsReferenceFormat Masks(string name, int bytes, uint red, uint green, uint blue, uint alpha, bool gray) => new(name, bytes, 0, (p, rgba) =>
    {
        var value = Read(p);
        var r = red == 0 ? 0 : Field(value, red);
        var a = alpha == 0 ? 1 : Field(value, alpha);
        if (gray)
        {
            SetRgba(rgba, r, r, r, a);
        }
        else
        {
            SetRgba(rgba, r, green == 0 ? 0 : Field(value, green), blue == 0 ? 0 : Field(value, blue), a);
        }
    });

    private static DdsReferenceFormat Unorm16(string name, int channels) => new(name, channels * 2, 0, (p, rgba) =>
    {
        Span<double> values = [0, 0, 0, 1];
        for (var c = 0; c < channels; c++)
        {
            values[c] = BinaryPrimitives.ReadUInt16LittleEndian(p[(c * 2)..]) / 65535.0;
        }

        Present(values, channels, rgba);
    });

    private static DdsReferenceFormat Halves(string name, int channels) => new(name, channels * 2, 0, (p, rgba) =>
    {
        Span<double> values = [0, 0, 0, 1];
        for (var c = 0; c < channels; c++)
        {
            values[c] = (double)BinaryPrimitives.ReadHalfLittleEndian(p[(c * 2)..]);
        }

        Present(values, channels, rgba);
    });

    private static DdsReferenceFormat Floats(string name, int channels) => new(name, channels * 4, 0, (p, rgba) =>
    {
        Span<double> values = [0, 0, 0, 1];
        for (var c = 0; c < channels; c++)
        {
            values[c] = BinaryPrimitives.ReadSingleLittleEndian(p[(c * 4)..]);
        }

        Present(values, channels, rgba);
    });

    /// <summary>One channel is gray, two are red and green with a blue of 0, three or four are as they are.</summary>
    private static void Present(ReadOnlySpan<double> values, int channels, Span<double> rgba)
    {
        if (channels == 1)
        {
            SetRgba(rgba, values[0], values[0], values[0], 1);
        }
        else
        {
            SetRgba(rgba, values[0], values[1], values[2], values[3]);
        }
    }

    /// <summary>An unsigned float of a 5-bit exponent (bias 15) and a mantissa of <paramref name="mantissaBits"/> bits.</summary>
    private static double SmallFloat(uint value, int mantissaBits)
    {
        var exponent = (int)(value >> mantissaBits);
        var mantissa = value & ((1u << mantissaBits) - 1);
        if (exponent == 31)
            return mantissa == 0 ? double.PositiveInfinity : double.NaN;

        return exponent == 0
            ? mantissa * Math.Pow(2, -14 - mantissaBits)
            : (1 + (mantissa / Math.Pow(2, mantissaBits))) * Math.Pow(2, exponent - 15);
    }

    private static void SetRgba(Span<double> rgba, double r, double g, double b, double a)
    {
        rgba[0] = r;
        rgba[1] = g;
        rgba[2] = b;
        rgba[3] = a;
    }
}
