using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>A directed segment of a closed contour, from its first point to its second point.</summary>
/// <param name="X0">The horizontal coordinate of the first point.</param>
/// <param name="Y0">The vertical coordinate of the first point.</param>
/// <param name="X1">The horizontal coordinate of the second point.</param>
/// <param name="Y1">The vertical coordinate of the second point.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ReferenceEdge(double X0, double Y0, double X1, double Y1);
