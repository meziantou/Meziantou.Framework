using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>The size and the weight of a text of a chart. The family is the one of the chart (<see cref="ChartScene.FontFamily"/>).</summary>
/// <param name="Size">The size of the font, in pixels.</param>
/// <param name="Bold">Whether the text is bold.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ChartFont(double Size, bool Bold = false);
