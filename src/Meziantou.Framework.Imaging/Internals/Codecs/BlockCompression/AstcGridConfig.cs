using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A weight grid of an ASTC block, its weight range and the color range that remains for the endpoints (sections 23.10, 23.13).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AstcGridConfig(int GridWidth, int GridHeight, int WeightRange, int ColorRange, int BlockMode);
