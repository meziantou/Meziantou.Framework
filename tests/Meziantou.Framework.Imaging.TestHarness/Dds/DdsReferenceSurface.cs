namespace Meziantou.Framework.Imaging.TestHarness.Dds;

/// <summary>A surface of a DDS file: its element, face (-1 when the texture is not a cube map), level and slice, its size and where its bytes are.</summary>
public sealed record DdsReferenceSurface(int ArrayIndex, int Face, int MipLevel, int DepthSlice, int Width, int Height, long Offset, long Length);
