using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>A vertex of a triangle mesh shading (ISO 32000-2, 8.7.4.5.5): a point of the shading space and its color.</summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
/// <param name="Color">The color, in the color space of the shading.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PdfMeshVertex(double X, double Y, PdfColor Color);
