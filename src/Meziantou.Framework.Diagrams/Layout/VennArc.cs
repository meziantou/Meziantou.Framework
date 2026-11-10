namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>An arc of the outline of an intersection: from (<see cref="X2"/>, <see cref="Y2"/>) to (<see cref="X1"/>, <see cref="Y1"/>) on a circle.</summary>
internal sealed record VennArc(VennCircle Circle, double Width, double X1, double Y1, double X2, double Y2, bool Large);
