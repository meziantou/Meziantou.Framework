using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A value of a series of an XY chart, placed on the x axis: on a category, or on a number.</summary>
/// <param name="Index">The index of the value in its series.</param>
/// <param name="Category">The category of a band axis, or <see langword="null"/> on a linear axis.</param>
/// <param name="X">The number of a linear axis.</param>
/// <param name="Value">The value.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct XyChartPoint(int Index, string? Category, double X, double Value);
