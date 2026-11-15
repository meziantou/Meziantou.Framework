namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The header <see cref="DdsEncoder"/> writes after <c>DDS_HEADER</c>.</summary>
public enum DdsHeaderVersion
{
    /// <summary>
    /// The legacy header (a FourCC or bit masks in <c>DDS_PIXELFORMAT</c>) when it can describe the texture, which the
    /// readers of Direct3D 9 also understand; otherwise the <c>DDS_HEADER_DXT10</c> extension. This is the default.
    /// </summary>
    /// <remarks>
    /// The legacy header describes the 8-bit RGBA and BGRA formats, R8, R8G8, R16, R16G16B16A16 (UNORM and FLOAT),
    /// R32G32B32A32_FLOAT, BC1 to BC3 (<c>DXT1</c>, <c>DXT3</c>, <c>DXT5</c>) and BC4 and BC5 (<c>BC4U</c>, <c>BC4S</c>,
    /// <c>ATI2</c>, <c>BC5S</c>), for textures that are not arrays. The <c>_SRGB</c> formats, BC6H and BC7, and texture
    /// arrays need the extension.
    /// </remarks>
    Automatic = 0,

    /// <summary>The legacy header only: a texture it cannot describe is rejected with an <see cref="UnsupportedImageFeatureException"/>.</summary>
    Legacy = 1,

    /// <summary>Always the <c>DDS_HEADER_DXT10</c> extension (FourCC <c>DX10</c>), which only Direct3D 10 and later readers understand.</summary>
    Dx10 = 2,
}
