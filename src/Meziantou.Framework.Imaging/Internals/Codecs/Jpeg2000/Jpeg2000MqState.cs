using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The state of <see cref="Jpeg2000MqEncoder"/> between two decisions.</summary>
/// <param name="Written">The number of bytes written to the codeword segment.</param>
/// <param name="Last">The last byte written, before the carries that may still reach it; 0 when no byte was written.</param>
/// <param name="C">The code register.</param>
/// <param name="A">The interval register.</param>
/// <param name="Count">The number of shifts before the next byte is written (CT).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000MqState(int Written, byte Last, uint C, uint A, int Count);
