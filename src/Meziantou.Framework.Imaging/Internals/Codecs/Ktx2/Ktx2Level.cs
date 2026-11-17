using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One entry of the level index (KTX section 3.9.7).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Ktx2Level(long Offset, long Length, long UncompressedLength);
