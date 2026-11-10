using System.Runtime.InteropServices;

namespace Meziantou.Framework.Mermaid;

/// <summary>The values an axis of an <see cref="XyChart"/> goes through, from <paramref name="Start"/> to <paramref name="End"/> (Mermaid <c>start --&gt; end</c>).</summary>
/// <param name="Start">The value at the start of the axis: at the bottom of a vertical axis, on the left of a horizontal one.</param>
/// <param name="End">The value at the end of the axis. It can be lower than <paramref name="Start"/>, which reverses the axis.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct XyChartAxisRange(double Start, double End);
