namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The operation modes of ASTC decoding (Khronos Data Format Specification 1.4, section 23.5).</summary>
internal enum AstcProfile
{
    /// <summary>sRGB operation mode: 8-bit results of the endpoints expanded with 0x80.</summary>
    Srgb,

    /// <summary>Linear LDR operation mode: the 16-bit (UNORM16) results of the interpolation.</summary>
    Linear,

    /// <summary>HDR operation mode, decode_float16: half-precision results.</summary>
    Hdr,
}
