using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The colors of an axis of an XY chart.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct XyChartAxisColors(SceneColor Title, SceneColor Label, SceneColor Tick, SceneColor Line);
