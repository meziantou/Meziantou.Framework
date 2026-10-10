using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The fields of the VP8 key-frame header.</summary>
/// <param name="Width">The frame width (1 to 16,383; the upscaling bits are ignored).</param>
/// <param name="Height">The frame height (1 to 16,383).</param>
/// <param name="FirstPartitionSize">The size of the first partition (modes and frame header), which follows the 10-byte key-frame header.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Vp8FrameHeader(int Width, int Height, int FirstPartitionSize);
