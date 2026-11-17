namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the texels of a texture written by the DDS or the KTX 2.0 encoder are stored.</summary>
internal enum TextureEncoding
{
    Rgba8,
    Bgra8,
    R8,
    Rg8,
    R16,
    Rgba16,
    RgbaHalf,
    RgbaFloat,
    Bc1,
    Bc1Opaque,
    Bc2,
    Bc3,
    Bc4,
    Bc4Signed,
    Bc5,
    Bc5Signed,
    Bc6H,
    Bc6HSigned,
    Bc7,
    Etc2Rgb,
    Etc2RgbA1,
    Etc2Rgba,
    EacR11,
    EacR11Signed,
    EacRg11,
    EacRg11Signed,
    Astc,
}
