using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>A tick label of an axis, measured and maybe shortened.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AxisLabel(double Position, string Text, TextMetrics Metrics);
