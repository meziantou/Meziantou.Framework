using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A component of a JPEG scan.</summary>
/// <param name="Component">The index of the component in the frame.</param>
/// <param name="DcTable">The destination of its DC table.</param>
/// <param name="AcTable">The destination of its AC table.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegBitstreamScanComponent(int Component, int DcTable, int AcTable);
