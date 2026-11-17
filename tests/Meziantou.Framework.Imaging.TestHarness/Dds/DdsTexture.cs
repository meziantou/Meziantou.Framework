namespace Meziantou.Framework.Imaging.TestHarness.Dds;

/// <summary>The headers and the surfaces of a DDS file.</summary>
public sealed record DdsTexture(int Width, int Height, int Depth, int MipCount, int ArraySize, IReadOnlyList<int> Faces, DdsReferenceFormat Format, int HeaderLength, IReadOnlyList<DdsReferenceSurface> Surfaces);
