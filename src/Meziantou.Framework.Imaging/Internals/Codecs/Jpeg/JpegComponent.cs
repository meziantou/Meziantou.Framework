using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A component of a JPEG frame header.</summary>
/// <param name="Id">The component identifier.</param>
/// <param name="HorizontalSampling">The horizontal sampling factor (1 to 4).</param>
/// <param name="VerticalSampling">The vertical sampling factor (1 to 4).</param>
/// <param name="QuantizationTable">The quantization table selector (0 to 3).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegComponent(byte Id, byte HorizontalSampling, byte VerticalSampling, byte QuantizationTable);
