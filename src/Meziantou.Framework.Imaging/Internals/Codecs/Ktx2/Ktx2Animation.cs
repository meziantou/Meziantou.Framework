using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The animation of an array texture (KTX section 5.8): each layer is a frame.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Ktx2Animation(uint Duration, uint Timescale, uint LoopCount);
