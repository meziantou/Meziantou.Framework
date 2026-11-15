using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Identifies one image of a texture: a depth slice of a mip level of a cube face of an array layer
/// (<see cref="ImageCollectionKind.Surfaces"/>).
/// </summary>
/// <param name="MipLevel">The mip level: 0 is the base level, each next level halves the size (rounded down, at least 1).</param>
/// <param name="ArrayLayer">The array layer: 0 for a texture that is not an array.</param>
/// <param name="Face">The cube face, 0 to 5 in the order +X, -X, +Y, -Y, +Z, -Z: 0 for a texture that is not a cube map.</param>
/// <param name="DepthSlice">The depth slice of a three-dimensional texture, from 0 to the depth of the level: 0 for a texture that is not three-dimensional.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TextureSubresource(int MipLevel, int ArrayLayer, int Face, int DepthSlice);
