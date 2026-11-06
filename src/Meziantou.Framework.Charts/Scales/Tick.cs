using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>A tick of an axis: its position on the range of the scale and its label.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Tick(double Position, string Label);
