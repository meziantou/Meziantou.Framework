using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A <see cref="FilterLightSource"/> placed on the canvas: the unit vector toward a distant light, or the position of a light in pixels of the canvas, and the axis of a spot.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct FilterLight
{
    public FilterLightKind Kind;
    public double X;
    public double Y;
    public double Z;
    public double AxisX;
    public double AxisY;
    public double AxisZ;
    public double Exponent;
    public double ConeCosine;
}
