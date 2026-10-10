using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A prefix code built by <see cref="Vp8LPrefixCodes"/>: the offset of its root table in the arena and its root bits.</summary>
/// <param name="Offset">The offset of the root table in the arena, in entries.</param>
/// <param name="RootBits">The number of bits indexing the root table (0 for a single-symbol code).</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Vp8LPrefixCode(int Offset, int RootBits);
