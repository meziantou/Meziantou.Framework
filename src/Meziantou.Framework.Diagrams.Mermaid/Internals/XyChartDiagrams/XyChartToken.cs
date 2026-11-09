using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.XyChartDiagrams;

/// <summary>A token of an XY chart: its kind, its text, and where it starts.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct XyChartToken(XyChartTokenKind Kind, string Text, int Start);
