using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One code word of the CCITT facsimile compressions.</summary>
/// <param name="Bits">The bits of the code, in the least significant positions; the most significant of them comes first.</param>
/// <param name="Length">The number of bits.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TiffCcittCode(uint Bits, int Length);
