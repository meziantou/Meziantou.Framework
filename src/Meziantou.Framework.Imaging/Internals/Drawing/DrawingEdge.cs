using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A non-horizontal segment of the outline being rasterized, stored from top (<see cref="Y0"/>) to bottom
/// (<see cref="Y1"/>, greater than <see cref="Y0"/>) and clipped to the rows of the frame.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DrawingEdge
{
    public double X0;
    public double Y0;
    public double X1;
    public double Y1;

    /// <summary>The horizontal distance per unit of height, <c>(X1 - X0) / (Y1 - Y0)</c>.</summary>
    public double Slope;

    /// <summary>The contribution to the winding number of the points to the right of the edge: +1 when the contour goes downward, -1 upward.</summary>
    public int Direction;
}
