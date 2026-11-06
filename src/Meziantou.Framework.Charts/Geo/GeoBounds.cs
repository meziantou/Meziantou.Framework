using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>A box of longitudes and latitudes, in degrees.</summary>
/// <param name="West">The smallest longitude.</param>
/// <param name="South">The smallest latitude.</param>
/// <param name="East">The largest longitude.</param>
/// <param name="North">The largest latitude.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoBounds(double West, double South, double East, double North);
