using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One image of a KTX 2.0 texture: a depth slice of a mip level of a cube face of an array layer.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Ktx2Subresource(int MipLevel, int ArrayLayer, int Face, int DepthSlice)
{
    /// <summary>Gets the public description of the subresource: the cube face is only set for a cube map.</summary>
    public TextureSurface ToTextureSurface(bool isCubeMap) => new(ArrayLayer, isCubeMap ? (CubeMapFace)Face : null, MipLevel, DepthSlice);
}
