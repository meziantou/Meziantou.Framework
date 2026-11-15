namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// The <c>DXGI_FORMAT</c> of the surfaces <see cref="DdsEncoder"/> writes: uncompressed pixels, or blocks of 4×4 texels
/// compressed with BC1 to BC7.
/// </summary>
/// <remarks>
/// The names are those of <c>DXGI_FORMAT</c> without their prefix. A format with fewer channels than the image, less
/// precision, or no alpha follows the rules of <see cref="DdsEncoder"/> (<see cref="DdsEncoder.AllowChannelReduction"/>,
/// <see cref="DdsEncoder.AllowBitDepthReduction"/>).
/// </remarks>
public enum DdsTextureFormat
{
    /// <summary>Four 8-bit channels, red first (<c>DXGI_FORMAT_R8G8B8A8_UNORM</c>, 28).</summary>
    R8G8B8A8Unorm = 0,

    /// <summary>Four 8-bit channels, red first, sRGB-encoded colors (<c>DXGI_FORMAT_R8G8B8A8_UNORM_SRGB</c>, 29).</summary>
    R8G8B8A8UnormSrgb = 1,

    /// <summary>Four 8-bit channels, blue first (<c>DXGI_FORMAT_B8G8R8A8_UNORM</c>, 87).</summary>
    B8G8R8A8Unorm = 2,

    /// <summary>Four 8-bit channels, blue first, sRGB-encoded colors (<c>DXGI_FORMAT_B8G8R8A8_UNORM_SRGB</c>, 91).</summary>
    B8G8R8A8UnormSrgb = 3,

    /// <summary>One 8-bit channel, read as gray (<c>DXGI_FORMAT_R8_UNORM</c>, 61).</summary>
    R8Unorm = 4,

    /// <summary>Two 8-bit channels, red and green (<c>DXGI_FORMAT_R8G8_UNORM</c>, 49).</summary>
    R8G8Unorm = 5,

    /// <summary>One 16-bit channel, read as gray (<c>DXGI_FORMAT_R16_UNORM</c>, 56).</summary>
    R16Unorm = 6,

    /// <summary>Four 16-bit channels (<c>DXGI_FORMAT_R16G16B16A16_UNORM</c>, 11).</summary>
    R16G16B16A16Unorm = 7,

    /// <summary>Four half-precision floating-point channels, linear light (<c>DXGI_FORMAT_R16G16B16A16_FLOAT</c>, 10).</summary>
    R16G16B16A16Float = 8,

    /// <summary>Four single-precision floating-point channels, linear light (<c>DXGI_FORMAT_R32G32B32A32_FLOAT</c>, 2).</summary>
    R32G32B32A32Float = 9,

    /// <summary>BC1 (DXT1): 5:6:5 colors with 1-bit alpha, 4 bits per texel (<c>DXGI_FORMAT_BC1_UNORM</c>, 71).</summary>
    Bc1Unorm = 10,

    /// <summary>BC1 with sRGB-encoded colors (<c>DXGI_FORMAT_BC1_UNORM_SRGB</c>, 72).</summary>
    Bc1UnormSrgb = 11,

    /// <summary>BC2 (DXT3): the colors of BC1 and 4-bit alpha, 8 bits per texel (<c>DXGI_FORMAT_BC2_UNORM</c>, 74).</summary>
    Bc2Unorm = 12,

    /// <summary>BC2 with sRGB-encoded colors (<c>DXGI_FORMAT_BC2_UNORM_SRGB</c>, 75).</summary>
    Bc2UnormSrgb = 13,

    /// <summary>BC3 (DXT5): the colors of BC1 and interpolated alpha, 8 bits per texel (<c>DXGI_FORMAT_BC3_UNORM</c>, 77).</summary>
    Bc3Unorm = 14,

    /// <summary>BC3 with sRGB-encoded colors (<c>DXGI_FORMAT_BC3_UNORM_SRGB</c>, 78).</summary>
    Bc3UnormSrgb = 15,

    /// <summary>BC4: one channel, read as gray, 4 bits per texel (<c>DXGI_FORMAT_BC4_UNORM</c>, 80).</summary>
    Bc4Unorm = 16,

    /// <summary>BC4 of signed values in [-1, 1] (<c>DXGI_FORMAT_BC4_SNORM</c>, 81).</summary>
    Bc4Snorm = 17,

    /// <summary>BC5: two channels, red and green, 8 bits per texel (<c>DXGI_FORMAT_BC5_UNORM</c>, 83), such as a normal map.</summary>
    Bc5Unorm = 18,

    /// <summary>BC5 of signed values in [-1, 1] (<c>DXGI_FORMAT_BC5_SNORM</c>, 84).</summary>
    Bc5Snorm = 19,

    /// <summary>BC6H: unsigned half-precision floating-point colors, high dynamic range, 8 bits per texel (<c>DXGI_FORMAT_BC6H_UF16</c>, 95).</summary>
    Bc6HUf16 = 20,

    /// <summary>BC6H: signed half-precision floating-point colors (<c>DXGI_FORMAT_BC6H_SF16</c>, 96).</summary>
    Bc6HSf16 = 21,

    /// <summary>BC7: 8-bit colors and alpha, 8 bits per texel (<c>DXGI_FORMAT_BC7_UNORM</c>, 98).</summary>
    Bc7Unorm = 22,

    /// <summary>BC7 with sRGB-encoded colors (<c>DXGI_FORMAT_BC7_UNORM_SRGB</c>, 99).</summary>
    Bc7UnormSrgb = 23,
}
