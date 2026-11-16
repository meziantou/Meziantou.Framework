namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the texels of a DDS surface are stored.</summary>
internal enum DdsEncoding
{
    /// <summary>BC1 (DXT1): 8-byte blocks of colors with 1-bit alpha.</summary>
    Bc1,

    /// <summary>BC2 (DXT3): 16-byte blocks of 4-bit alpha and colors.</summary>
    Bc2,

    /// <summary>BC3 (DXT5): 16-byte blocks of interpolated alpha and colors.</summary>
    Bc3,

    /// <summary>BC4 unsigned: 8-byte blocks of one channel.</summary>
    Bc4,

    /// <summary>BC4 signed.</summary>
    Bc4Signed,

    /// <summary>BC5 unsigned: 16-byte blocks of two channels.</summary>
    Bc5,

    /// <summary>BC5 signed.</summary>
    Bc5Signed,

    /// <summary>BC6H unsigned (UF16): 16-byte blocks of half-float colors.</summary>
    Bc6H,

    /// <summary>BC6H signed (SF16).</summary>
    Bc6HSigned,

    /// <summary>BC7: 16-byte blocks of colors and alpha.</summary>
    Bc7,

    /// <summary>Uncompressed pixels of 8 to 32 bits whose channels are given by masks.</summary>
    Masks,

    /// <summary>Four 16-bit unsigned normalized channels (R16G16B16A16_UNORM).</summary>
    Rgba16,

    /// <summary>One to four 16-bit floating-point channels.</summary>
    Half,

    /// <summary>One to four 32-bit floating-point channels.</summary>
    Float,

    /// <summary>R11G11B10_FLOAT: unsigned floats of 11, 11 and 10 bits.</summary>
    R11G11B10Float,

    /// <summary>R9G9B9E5_SHAREDEXP: three 9-bit mantissas and a shared 5-bit exponent.</summary>
    R9G9B9E5,
}
