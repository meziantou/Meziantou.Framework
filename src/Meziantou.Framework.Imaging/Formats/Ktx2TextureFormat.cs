namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// The format of the texels <see cref="Ktx2Encoder"/> writes: uncompressed, or block-compressed with BCn, ETC2, EAC or ASTC.
/// </summary>
/// <remarks>
/// The members name a family of <c>VkFormat</c> values: the transfer function of the image chooses between the <c>_SRGB</c>
/// and the <c>_UNORM</c> member of a family that has both (an image labeled
/// <see cref="Metadata.ColorTransferFunction.Linear"/> is written <c>_UNORM</c> with a linear transfer function, any other
/// image <c>_SRGB</c>). A format without an <c>_SRGB</c> variant is written <c>_UNORM</c>, and its data format descriptor
/// carries the transfer function of the image.
/// </remarks>
public enum Ktx2TextureFormat
{
    /// <summary>One 8-bit channel (<c>R8_UNORM</c> or <c>R8_SRGB</c>), read as gray.</summary>
    R8 = 0,

    /// <summary>Two 8-bit channels, red and green (<c>R8G8_UNORM</c> or <c>R8G8_SRGB</c>).</summary>
    R8G8 = 1,

    /// <summary>Four 8-bit channels (<c>R8G8B8A8_UNORM</c> or <c>R8G8B8A8_SRGB</c>).</summary>
    R8G8B8A8 = 2,

    /// <summary>One 16-bit channel (<c>R16_UNORM</c>), read as gray.</summary>
    R16 = 3,

    /// <summary>Four 16-bit channels (<c>R16G16B16A16_UNORM</c>).</summary>
    R16G16B16A16 = 4,

    /// <summary>Four half-precision floating-point channels (<c>R16G16B16A16_SFLOAT</c>), linear light.</summary>
    R16G16B16A16Sfloat = 5,

    /// <summary>Four single-precision floating-point channels (<c>R32G32B32A32_SFLOAT</c>), linear light.</summary>
    R32G32B32A32Sfloat = 6,

    /// <summary>BC1 without alpha (<c>BC1_RGB_UNORM_BLOCK</c> or <c>BC1_RGB_SRGB_BLOCK</c>).</summary>
    Bc1Rgb = 7,

    /// <summary>BC1 with 1-bit alpha (<c>BC1_RGBA_UNORM_BLOCK</c> or <c>BC1_RGBA_SRGB_BLOCK</c>).</summary>
    Bc1Rgba = 8,

    /// <summary>BC2: 4-bit alpha (<c>BC2_UNORM_BLOCK</c> or <c>BC2_SRGB_BLOCK</c>).</summary>
    Bc2 = 9,

    /// <summary>BC3: interpolated alpha (<c>BC3_UNORM_BLOCK</c> or <c>BC3_SRGB_BLOCK</c>).</summary>
    Bc3 = 10,

    /// <summary>BC4: one channel (<c>BC4_UNORM_BLOCK</c>), read as gray.</summary>
    Bc4Unorm = 11,

    /// <summary>BC4 of signed values in [-1, 1] (<c>BC4_SNORM_BLOCK</c>).</summary>
    Bc4Snorm = 12,

    /// <summary>BC5: red and green (<c>BC5_UNORM_BLOCK</c>).</summary>
    Bc5Unorm = 13,

    /// <summary>BC5 of signed values in [-1, 1] (<c>BC5_SNORM_BLOCK</c>).</summary>
    Bc5Snorm = 14,

    /// <summary>BC6H of unsigned half-precision floating-point colors (<c>BC6H_UFLOAT_BLOCK</c>).</summary>
    Bc6HUfloat = 15,

    /// <summary>BC6H of signed half-precision floating-point colors (<c>BC6H_SFLOAT_BLOCK</c>).</summary>
    Bc6HSfloat = 16,

    /// <summary>BC7 (<c>BC7_UNORM_BLOCK</c> or <c>BC7_SRGB_BLOCK</c>).</summary>
    Bc7 = 17,

    /// <summary>ETC2 colors without alpha (<c>ETC2_R8G8B8_UNORM_BLOCK</c> or <c>ETC2_R8G8B8_SRGB_BLOCK</c>).</summary>
    Etc2Rgb = 18,

    /// <summary>ETC2 colors with punchthrough (1-bit) alpha (<c>ETC2_R8G8B8A1_UNORM_BLOCK</c> or <c>ETC2_R8G8B8A1_SRGB_BLOCK</c>).</summary>
    Etc2RgbA1 = 19,

    /// <summary>ETC2 colors with an EAC alpha block (<c>ETC2_R8G8B8A8_UNORM_BLOCK</c> or <c>ETC2_R8G8B8A8_SRGB_BLOCK</c>).</summary>
    Etc2Rgba = 20,

    /// <summary>One 11-bit channel (<c>EAC_R11_UNORM_BLOCK</c>), read as gray.</summary>
    EacR11Unorm = 21,

    /// <summary>One signed 11-bit channel of values in [-1, 1] (<c>EAC_R11_SNORM_BLOCK</c>).</summary>
    EacR11Snorm = 22,

    /// <summary>Two 11-bit channels, red and green (<c>EAC_R11G11_UNORM_BLOCK</c>).</summary>
    EacR11G11Unorm = 23,

    /// <summary>Two signed 11-bit channels of values in [-1, 1] (<c>EAC_R11G11_SNORM_BLOCK</c>).</summary>
    EacR11G11Snorm = 24,

    /// <summary>ASTC LDR, blocks of 4×4 texels: 8 bits per texel (<c>ASTC_4x4_UNORM_BLOCK</c> or <c>ASTC_4x4_SRGB_BLOCK</c>).</summary>
    Astc4x4 = 25,

    /// <summary>ASTC LDR, blocks of 5×4 texels: 6.40 bits per texel.</summary>
    Astc5x4 = 26,

    /// <summary>ASTC LDR, blocks of 5×5 texels: 5.12 bits per texel.</summary>
    Astc5x5 = 27,

    /// <summary>ASTC LDR, blocks of 6×5 texels: 4.27 bits per texel.</summary>
    Astc6x5 = 28,

    /// <summary>ASTC LDR, blocks of 6×6 texels: 3.56 bits per texel.</summary>
    Astc6x6 = 29,

    /// <summary>ASTC LDR, blocks of 8×5 texels: 3.20 bits per texel.</summary>
    Astc8x5 = 30,

    /// <summary>ASTC LDR, blocks of 8×6 texels: 2.67 bits per texel.</summary>
    Astc8x6 = 31,

    /// <summary>ASTC LDR, blocks of 8×8 texels: 2 bits per texel.</summary>
    Astc8x8 = 32,

    /// <summary>ASTC LDR, blocks of 10×5 texels: 2.56 bits per texel.</summary>
    Astc10x5 = 33,

    /// <summary>ASTC LDR, blocks of 10×6 texels: 2.13 bits per texel.</summary>
    Astc10x6 = 34,

    /// <summary>ASTC LDR, blocks of 10×8 texels: 1.60 bits per texel.</summary>
    Astc10x8 = 35,

    /// <summary>ASTC LDR, blocks of 10×10 texels: 1.28 bits per texel.</summary>
    Astc10x10 = 36,

    /// <summary>ASTC LDR, blocks of 12×10 texels: 1.07 bits per texel.</summary>
    Astc12x10 = 37,

    /// <summary>ASTC LDR, blocks of 12×12 texels: 0.89 bits per texel.</summary>
    Astc12x12 = 38,
}
