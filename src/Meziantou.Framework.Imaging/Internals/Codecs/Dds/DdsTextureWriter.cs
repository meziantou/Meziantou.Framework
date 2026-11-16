using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the headers of a DDS file (<c>DDS_HEADER</c>, <c>DDS_PIXELFORMAT</c> and <c>DDS_HEADER_DXT10</c>, from the DDS
/// reference of Microsoft Learn) and orders its surfaces: for each array element, each face of a cube map, each mipmap
/// level from the largest.
/// </summary>
internal static class DdsTextureWriter
{
    private const uint FlagCaps = 0x1;
    private const uint FlagHeight = 0x2;
    private const uint FlagWidth = 0x4;
    private const uint FlagPixelFormat = 0x1000;
    private const uint FlagMipmapCount = 0x20000;
    private const uint CapsComplex = 0x8;
    private const uint CapsTexture = 0x1000;
    private const uint CapsMipmap = 0x400000;

    // D3DFORMAT values of the Direct3D 9 floating-point and 16-bit formats, written as FourCC values
    private const uint D3dA16B16G16R16 = 36;
    private const uint D3dA16B16G16R16F = 113;
    private const uint D3dA32B32G32R32F = 116;

    /// <summary>Gets the format of the default output of a pixel format: uncompressed, without loss.</summary>
    public static DdsTextureFormat GetDefaultFormat(PixelFormat pixelFormat) => pixelFormat switch
    {
        PixelFormat.Gray8 => DdsTextureFormat.R8Unorm,
        PixelFormat.Gray16 => DdsTextureFormat.R16Unorm,
        PixelFormat.RgbaFloat => DdsTextureFormat.R32G32B32A32Float,
        PixelFormat.RgbaHalf => DdsTextureFormat.R16G16B16A16Float,
        _ when PixelFormats.GetBitsPerComponent(pixelFormat) > 8 => DdsTextureFormat.R16G16B16A16Unorm,
        _ => DdsTextureFormat.R8G8B8A8Unorm,
    };

    /// <summary>Gets the format an encoder writes for pixels of a format.</summary>
    public static DdsTextureFormat ResolveFormat(DdsEncoder encoder, PixelFormat pixelFormat) => encoder.TextureFormat ?? GetDefaultFormat(pixelFormat);

    /// <summary>Determines whether a format stores floating-point or signed samples, which are linear light.</summary>
    public static bool IsFloatingPoint(DdsTextureFormat format) => GetTarget(format).IsFloatingPoint;

    public static TextureTarget GetTarget(DdsTextureFormat format) => format switch
    {
        DdsTextureFormat.R8G8B8A8Unorm => TextureTarget.Create(TextureEncoding.Rgba8),
        DdsTextureFormat.R8G8B8A8UnormSrgb => TextureTarget.Create(TextureEncoding.Rgba8, isSrgb: true),
        DdsTextureFormat.B8G8R8A8Unorm => TextureTarget.Create(TextureEncoding.Bgra8),
        DdsTextureFormat.B8G8R8A8UnormSrgb => TextureTarget.Create(TextureEncoding.Bgra8, isSrgb: true),
        DdsTextureFormat.R8Unorm => TextureTarget.Create(TextureEncoding.R8),
        DdsTextureFormat.R8G8Unorm => TextureTarget.Create(TextureEncoding.Rg8),
        DdsTextureFormat.R16Unorm => TextureTarget.Create(TextureEncoding.R16),
        DdsTextureFormat.R16G16B16A16Unorm => TextureTarget.Create(TextureEncoding.Rgba16),
        DdsTextureFormat.R16G16B16A16Float => TextureTarget.Create(TextureEncoding.RgbaHalf),
        DdsTextureFormat.R32G32B32A32Float => TextureTarget.Create(TextureEncoding.RgbaFloat),
        DdsTextureFormat.Bc1Unorm => TextureTarget.Create(TextureEncoding.Bc1),
        DdsTextureFormat.Bc1UnormSrgb => TextureTarget.Create(TextureEncoding.Bc1, isSrgb: true),
        DdsTextureFormat.Bc2Unorm => TextureTarget.Create(TextureEncoding.Bc2),
        DdsTextureFormat.Bc2UnormSrgb => TextureTarget.Create(TextureEncoding.Bc2, isSrgb: true),
        DdsTextureFormat.Bc3Unorm => TextureTarget.Create(TextureEncoding.Bc3),
        DdsTextureFormat.Bc3UnormSrgb => TextureTarget.Create(TextureEncoding.Bc3, isSrgb: true),
        DdsTextureFormat.Bc4Unorm => TextureTarget.Create(TextureEncoding.Bc4),
        DdsTextureFormat.Bc4Snorm => TextureTarget.Create(TextureEncoding.Bc4Signed),
        DdsTextureFormat.Bc5Unorm => TextureTarget.Create(TextureEncoding.Bc5),
        DdsTextureFormat.Bc5Snorm => TextureTarget.Create(TextureEncoding.Bc5Signed),
        DdsTextureFormat.Bc6HUf16 => TextureTarget.Create(TextureEncoding.Bc6H),
        DdsTextureFormat.Bc6HSf16 => TextureTarget.Create(TextureEncoding.Bc6HSigned),
        DdsTextureFormat.Bc7Unorm => TextureTarget.Create(TextureEncoding.Bc7),
        DdsTextureFormat.Bc7UnormSrgb => TextureTarget.Create(TextureEncoding.Bc7, isSrgb: true),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a DDS texture format."),
    };

    /// <summary>Gets the <c>DXGI_FORMAT</c> value of a format.</summary>
    public static uint GetDxgiFormat(DdsTextureFormat format) => format switch
    {
        DdsTextureFormat.R8G8B8A8Unorm => 28,
        DdsTextureFormat.R8G8B8A8UnormSrgb => 29,
        DdsTextureFormat.B8G8R8A8Unorm => 87,
        DdsTextureFormat.B8G8R8A8UnormSrgb => 91,
        DdsTextureFormat.R8Unorm => 61,
        DdsTextureFormat.R8G8Unorm => 49,
        DdsTextureFormat.R16Unorm => 56,
        DdsTextureFormat.R16G16B16A16Unorm => 11,
        DdsTextureFormat.R16G16B16A16Float => 10,
        DdsTextureFormat.R32G32B32A32Float => 2,
        DdsTextureFormat.Bc1Unorm => 71,
        DdsTextureFormat.Bc1UnormSrgb => 72,
        DdsTextureFormat.Bc2Unorm => 74,
        DdsTextureFormat.Bc2UnormSrgb => 75,
        DdsTextureFormat.Bc3Unorm => 77,
        DdsTextureFormat.Bc3UnormSrgb => 78,
        DdsTextureFormat.Bc4Unorm => 80,
        DdsTextureFormat.Bc4Snorm => 81,
        DdsTextureFormat.Bc5Unorm => 83,
        DdsTextureFormat.Bc5Snorm => 84,
        DdsTextureFormat.Bc6HUf16 => 95,
        DdsTextureFormat.Bc6HSf16 => 96,
        DdsTextureFormat.Bc7Unorm => 98,
        DdsTextureFormat.Bc7UnormSrgb => 99,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a DDS texture format."),
    };

    /// <summary>Determines whether the legacy header can describe a texture: its format, and no array.</summary>
    public static bool HasLegacyHeader(DdsTextureFormat format, bool isArray) => !isArray && GetLegacyPixelFormat(format) is not null;

    /// <summary>Resolves the header of a texture, and rejects a legacy header that cannot describe it.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The legacy header is requested and cannot describe the texture.</exception>
    public static bool UsesDx10Header(DdsTextureFormat format, bool isArray, DdsHeaderVersion version)
    {
        var legacy = HasLegacyHeader(format, isArray);
        return version switch
        {
            DdsHeaderVersion.Dx10 => true,
            DdsHeaderVersion.Legacy when !legacy => throw DdsFormat.Unsupported($"The legacy DDS header cannot describe {(isArray ? "a texture array" : "the format " + format)}: the _SRGB formats, BC6H, BC7 and arrays need the DX10 header. Set DdsEncoder.HeaderVersion to Automatic or Dx10.", "Legacy header"),
            _ => !legacy,
        };
    }

    /// <summary>Creates the magic number and the headers.</summary>
    public static byte[] CreateHeader(EncodedTexture texture, DdsTextureFormat format, bool dx10)
    {
        var header = new byte[DdsFormat.HeaderLength + (dx10 ? DdsFormat.Dx10HeaderLength : 0)];
        var span = header.AsSpan();
        DdsFormat.Magic.CopyTo(span);
        var target = texture.Target;
        var flags = FlagCaps | FlagHeight | FlagWidth | FlagPixelFormat;
        uint pitchOrLinearSize;
        if (target.IsBlockCompressed)
        {
            flags |= DdsFormat.FlagLinearSize;
            pitchOrLinearSize = (uint)texture.GetSurfaceLength(0);
        }
        else
        {
            flags |= DdsFormat.FlagPitch;
            pitchOrLinearSize = (uint)((long)texture.Width * target.BytesPerBlock);
        }

        if (texture.LevelCount > 1)
        {
            flags |= FlagMipmapCount;
        }

        Write(span, 4, DdsFormat.HeaderSize);
        Write(span, 8, flags);
        Write(span, 12, (uint)texture.Height);
        Write(span, 16, (uint)texture.Width);
        Write(span, 20, pitchOrLinearSize);
        Write(span, 28, (uint)texture.LevelCount);

        // DDS_PIXELFORMAT at offset 76
        Write(span, 76, DdsFormat.PixelFormatSize);
        if (dx10)
        {
            Write(span, 80, DdsFormat.PixelFourCC);
            Write(span, 84, DdsFormat.FourCC("DX10"));
        }
        else
        {
            var legacy = GetLegacyPixelFormat(format)!.Value;
            Write(span, 80, legacy.Flags);
            Write(span, 84, legacy.FourCC);
            Write(span, 88, legacy.BitCount);
            Write(span, 92, legacy.RedMask);
            Write(span, 96, legacy.GreenMask);
            Write(span, 100, legacy.BlueMask);
            Write(span, 104, legacy.AlphaMask);
        }

        var caps = CapsTexture;
        if (texture.LevelCount > 1)
        {
            caps |= CapsComplex | CapsMipmap;
        }

        var cube = texture.FaceCount == 6;
        if (cube)
        {
            caps |= CapsComplex;
            Write(span, 112, DdsFormat.Caps2CubeMap | DdsFormat.Caps2CubeMapAllFaces);
        }

        Write(span, 108, caps);
        if (dx10)
        {
            Write(span, 128, GetDxgiFormat(format));
            Write(span, 132, DdsFormat.DimensionTexture2D);
            Write(span, 136, cube ? DdsFormat.MiscTextureCube : 0);
            Write(span, 140, (uint)texture.LayerCount);

            // DDS_ALPHA_MODE_UNKNOWN: straight alpha, the default of readers
            Write(span, 144, 0);
        }

        return header;
    }

    /// <summary>Gets the surfaces in file order.</summary>
    public static IEnumerable<ReadOnlyMemory<byte>> GetSurfaces(EncodedTexture texture)
    {
        for (var layer = 0; layer < texture.LayerCount; layer++)
        {
            for (var face = 0; face < texture.FaceCount; face++)
            {
                for (var level = 0; level < texture.LevelCount; level++)
                {
                    yield return texture.Get(layer, face, level);
                }
            }
        }
    }

    private static void Write(Span<byte> header, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(header[offset..], value);

    private static LegacyPixelFormat? GetLegacyPixelFormat(DdsTextureFormat format) => format switch
    {
        DdsTextureFormat.R8G8B8A8Unorm => new(DdsFormat.PixelRgb | DdsFormat.PixelAlphaPixels, 0, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000),
        DdsTextureFormat.B8G8R8A8Unorm => new(DdsFormat.PixelRgb | DdsFormat.PixelAlphaPixels, 0, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000),
        DdsTextureFormat.R8Unorm => new(DdsFormat.PixelLuminance, 0, 8, 0xFF, 0, 0, 0),
        DdsTextureFormat.R8G8Unorm => new(DdsFormat.PixelRgb, 0, 16, 0xFF, 0xFF00, 0, 0),
        DdsTextureFormat.R16Unorm => new(DdsFormat.PixelLuminance, 0, 16, 0xFFFF, 0, 0, 0),
        DdsTextureFormat.R16G16B16A16Unorm => FourCC(D3dA16B16G16R16),
        DdsTextureFormat.R16G16B16A16Float => FourCC(D3dA16B16G16R16F),
        DdsTextureFormat.R32G32B32A32Float => FourCC(D3dA32B32G32R32F),
        DdsTextureFormat.Bc1Unorm => FourCC(DdsFormat.FourCC("DXT1")),
        DdsTextureFormat.Bc2Unorm => FourCC(DdsFormat.FourCC("DXT3")),
        DdsTextureFormat.Bc3Unorm => FourCC(DdsFormat.FourCC("DXT5")),
        DdsTextureFormat.Bc4Unorm => FourCC(DdsFormat.FourCC("BC4U")),
        DdsTextureFormat.Bc4Snorm => FourCC(DdsFormat.FourCC("BC4S")),
        DdsTextureFormat.Bc5Unorm => FourCC(DdsFormat.FourCC("ATI2")),
        DdsTextureFormat.Bc5Snorm => FourCC(DdsFormat.FourCC("BC5S")),
        _ => null,
    };

    private static LegacyPixelFormat FourCC(uint code) => new(DdsFormat.PixelFourCC, code, 0, 0, 0, 0, 0);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LegacyPixelFormat(uint Flags, uint FourCC, uint BitCount, uint RedMask, uint GreenMask, uint BlueMask, uint AlphaMask);
}
