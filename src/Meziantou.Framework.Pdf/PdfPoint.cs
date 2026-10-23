using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>A point of a coordinate space, such as the user space or the space of a shading.</summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PdfPoint(double X, double Y);
