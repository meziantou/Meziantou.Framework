using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>A point of a path, with the direction of the path there (<see cref="PathData.GetPointAtLength"/>).</summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
/// <param name="Angle">The direction of the path, in degrees, clockwise from the x axis (the y axis points down).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PathPoint(double X, double Y, double Angle);
