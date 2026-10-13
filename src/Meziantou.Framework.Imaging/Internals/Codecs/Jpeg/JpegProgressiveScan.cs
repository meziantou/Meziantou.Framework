using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A scan of a progressive JPEG: consecutive components, the band <c>Ss</c> to <c>Se</c> and the bit positions <c>Ah</c> and <c>Al</c> (ITU-T T.81 section G.1.1.1).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegProgressiveScan(byte FirstComponent, byte ComponentCount, byte Ss, byte Se, byte Ah, byte Al);
