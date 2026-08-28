using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// A pair of chunks, one on each side, that an anchor-based algorithm has decided to treat as a match and
/// diff around.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Anchor(int LeftIndex, int RightIndex);
