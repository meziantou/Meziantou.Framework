using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>A command of a <see cref="PathData"/>. The fields that do not apply to its kind are 0.</summary>
/// <param name="Kind">The kind of the command.</param>
/// <param name="X">The x coordinate of the end point.</param>
/// <param name="Y">The y coordinate of the end point.</param>
/// <param name="X1">The x coordinate of the first control point, or the x radius of an arc.</param>
/// <param name="Y1">The y coordinate of the first control point, or the y radius of an arc.</param>
/// <param name="X2">The x coordinate of the second control point, or the rotation of an arc in degrees.</param>
/// <param name="Y2">The y coordinate of the second control point.</param>
/// <param name="LargeArc">For an arc, whether it is the larger of the two candidate arcs.</param>
/// <param name="Sweep">For an arc, whether it is drawn clockwise.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PathCommand(PathCommandKind Kind, double X, double Y, double X1 = 0, double Y1 = 0, double X2 = 0, double Y2 = 0, bool LargeArc = false, bool Sweep = false);
