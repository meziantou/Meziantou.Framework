using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>A position on the Earth, in degrees.</summary>
/// <param name="Longitude">The longitude, from -180 (west) to 180 (east).</param>
/// <param name="Latitude">The latitude, from -90 (south) to 90 (north).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoPosition(double Longitude, double Latitude);
