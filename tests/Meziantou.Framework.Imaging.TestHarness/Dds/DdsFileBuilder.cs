using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Dds;

/// <summary>
/// Assembles DDS files from the fields of their headers and the bytes of their surfaces, for the tests and the fixtures that
/// need a file no tool writes (every legacy pixel format, partial cube maps, alpha modes, odd sizes, wrong pitches) and for
/// malformed files. It is written from the DDS reference of Microsoft Learn (<c>DDS_HEADER</c>, <c>DDS_PIXELFORMAT</c>,
/// <c>DDS_HEADER_DXT10</c>) and never uses the library under test. It writes what it is told to: nothing is validated.
/// </summary>
public sealed class DdsFileBuilder
{
    public const uint FlagCaps = 0x1;
    public const uint FlagHeight = 0x2;
    public const uint FlagWidth = 0x4;
    public const uint FlagPitch = 0x8;
    public const uint FlagPixelFormat = 0x1000;
    public const uint FlagMipMapCount = 0x20000;
    public const uint FlagLinearSize = 0x80000;
    public const uint FlagDepth = 0x800000;

    public const uint PixelAlphaPixels = 0x1;
    public const uint PixelAlpha = 0x2;
    public const uint PixelFourCC = 0x4;
    public const uint PixelRgb = 0x40;
    public const uint PixelYuv = 0x200;
    public const uint PixelLuminance = 0x20000;

    public const uint CapsComplex = 0x8;
    public const uint CapsTexture = 0x1000;
    public const uint CapsMipMap = 0x400000;

    public const uint Caps2CubeMap = 0x200;
    public const uint Caps2AllFaces = 0xFC00;
    public const uint Caps2Volume = 0x200000;

    /// <summary>Gets or sets the magic number (normally <c>DDS </c>).</summary>
    public uint Magic { get; set; } = FourCC("DDS ");

    public uint Size { get; set; } = 124;

    public uint Flags { get; set; } = FlagCaps | FlagHeight | FlagWidth | FlagPixelFormat;

    public uint Height { get; set; }

    public uint Width { get; set; }

    public uint PitchOrLinearSize { get; set; }

    public uint Depth { get; set; }

    public uint MipMapCount { get; set; }

    public uint PixelFormatSize { get; set; } = 32;

    public uint PixelFormatFlags { get; set; }

    public uint FourCCCode { get; set; }

    public uint RgbBitCount { get; set; }

    public uint RedMask { get; set; }

    public uint GreenMask { get; set; }

    public uint BlueMask { get; set; }

    public uint AlphaMask { get; set; }

    public uint Caps { get; set; } = CapsTexture;

    public uint Caps2 { get; set; }

    /// <summary>Gets or sets a value indicating whether a DX10 header follows (the FourCC is then <c>DX10</c>).</summary>
    public bool HasDx10Header { get; set; }

    public uint DxgiFormat { get; set; }

    public uint ResourceDimension { get; set; } = 3;

    public uint MiscFlag { get; set; }

    public uint ArraySize { get; set; } = 1;

    public uint MiscFlags2 { get; set; }

    /// <summary>Gets or sets the bytes of every surface, in file order.</summary>
    public ReadOnlyMemory<byte> Data { get; set; }

    /// <summary>The value of a four-character code.</summary>
    public static uint FourCC(string code) => code[0] | ((uint)code[1] << 8) | ((uint)code[2] << 16) | ((uint)code[3] << 24);

    /// <summary>Creates the builder of a texture of a DXGI format with a DX10 header.</summary>
    public static DdsFileBuilder Dx10(uint width, uint height, uint dxgiFormat, ReadOnlyMemory<byte> data) => new()
    {
        Width = width,
        Height = height,
        PixelFormatFlags = PixelFourCC,
        FourCCCode = FourCC("DX10"),
        HasDx10Header = true,
        DxgiFormat = dxgiFormat,
        Data = data,
    };

    /// <summary>Creates the builder of a texture of a legacy FourCC (DXT1, ATI2, 113...).</summary>
    public static DdsFileBuilder Legacy(uint width, uint height, uint fourCC, ReadOnlyMemory<byte> data) => new()
    {
        Width = width,
        Height = height,
        PixelFormatFlags = PixelFourCC,
        FourCCCode = fourCC,
        Data = data,
    };

    /// <summary>Creates the builder of a texture of legacy masks.</summary>
    public static DdsFileBuilder Masks(uint width, uint height, uint flags, uint bitCount, uint red, uint green, uint blue, uint alpha, ReadOnlyMemory<byte> data) => new()
    {
        Width = width,
        Height = height,
        PixelFormatFlags = flags,
        RgbBitCount = bitCount,
        RedMask = red,
        GreenMask = green,
        BlueMask = blue,
        AlphaMask = alpha,
        Data = data,
    };

    public byte[] Build()
    {
        var headerLength = HasDx10Header ? 148 : 128;
        var result = new byte[headerLength + Data.Length];
        var span = result.AsSpan();
        Write(span, 0, Magic);
        Write(span, 4, Size);
        Write(span, 8, Flags);
        Write(span, 12, Height);
        Write(span, 16, Width);
        Write(span, 20, PitchOrLinearSize);
        Write(span, 24, Depth);
        Write(span, 28, MipMapCount);
        Write(span, 76, PixelFormatSize);
        Write(span, 80, PixelFormatFlags);
        Write(span, 84, FourCCCode);
        Write(span, 88, RgbBitCount);
        Write(span, 92, RedMask);
        Write(span, 96, GreenMask);
        Write(span, 100, BlueMask);
        Write(span, 104, AlphaMask);
        Write(span, 108, Caps);
        Write(span, 112, Caps2);
        if (HasDx10Header)
        {
            Write(span, 128, DxgiFormat);
            Write(span, 132, ResourceDimension);
            Write(span, 136, MiscFlag);
            Write(span, 140, ArraySize);
            Write(span, 144, MiscFlags2);
        }

        Data.Span.CopyTo(span[headerLength..]);
        return result;
    }

    private static void Write(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
}
