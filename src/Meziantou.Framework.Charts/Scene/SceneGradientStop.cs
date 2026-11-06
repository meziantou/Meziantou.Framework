using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>A color stop of a gradient.</summary>
/// <param name="Offset">The position of the stop along the gradient, from 0 to 1.</param>
/// <param name="Color">The color at the stop.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneGradientStop(double Offset, ChartColor Color);
