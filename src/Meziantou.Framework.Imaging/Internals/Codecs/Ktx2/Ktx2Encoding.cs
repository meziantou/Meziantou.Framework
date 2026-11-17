namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the texel blocks of a KTX 2.0 format are encoded.</summary>
internal enum Ktx2Encoding
{
    /// <summary>One texel per block, its samples at the bit offsets of the data format descriptor.</summary>
    Uncompressed,

    /// <summary>ETC2 color blocks (RGB, RGB with punchthrough alpha, RGBA with an EAC alpha block).</summary>
    Etc2,

    /// <summary>EAC R11 or RG11 blocks, unsigned or signed.</summary>
    Eac,

    /// <summary>BCn blocks: S3TC (BC1, BC2, BC3), RGTC (BC4, BC5) and BPTC (BC6H, BC7), unsigned or signed.</summary>
    Bcn,

    /// <summary>ASTC blocks of two dimensions, LDR (UNORM, sRGB) or HDR (SFLOAT).</summary>
    Astc,

    /// <summary>ETC1S slices of the BasisLZ supercompression (VK_FORMAT_UNDEFINED, color model ETC1S).</summary>
    Etc1S,

    /// <summary>UASTC blocks (VK_FORMAT_UNDEFINED, color model UASTC).</summary>
    Uastc,
}
